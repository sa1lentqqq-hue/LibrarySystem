using System.Globalization;
using LibrarySystem.Data;

namespace LibrarySystem.Services;

/// <summary>
/// Бизнес-правила библиотеки: выдача, возврат, продление, утеря и штрафы.
/// Все проверки выполняются до записи, а изменения — внутри транзакции.
/// </summary>
public static class LoanService
{
    private static long Count(string sql, params (string, object?)[] args) => Database.Scalar<long>(sql, args);

    private static bool LoanExists(long loanId) => Count("SELECT COUNT(*) FROM loans WHERE id = @id", ("@id", loanId)) > 0;

    public static OpResult Issue(long readerId, long bookId)
    {
        if (Count("SELECT blocked FROM readers WHERE id = @id", ("@id", readerId)) == 1)
            return OpResult.Fail("Читатель заблокирован.");

        if (Count("SELECT COUNT(*) FROM fines f JOIN loans l ON l.id = f.loan_id WHERE l.reader_id = @id AND f.paid = 0",
                ("@id", readerId)) > 0)
            return OpResult.Fail("У читателя есть неоплаченные штрафы. Выдача невозможна.");

        if (Count("SELECT COUNT(*) FROM loans WHERE reader_id = @id AND returned_at IS NULL AND date(due_at) < date('now','localtime')",
                ("@id", readerId)) > 0)
            return OpResult.Fail("У читателя есть просроченные книги. Сначала нужно вернуть их.");

        if (Count("SELECT COUNT(*) FROM loans WHERE reader_id = @id AND returned_at IS NULL", ("@id", readerId)) >= AppConfig.MaxLoans)
            return OpResult.Fail($"Превышен лимит: не более {AppConfig.MaxLoans} книг на руках.");

        if (Count("SELECT COUNT(*) FROM loans WHERE reader_id = @r AND book_id = @b AND returned_at IS NULL",
                ("@r", readerId), ("@b", bookId)) > 0)
            return OpResult.Fail("Эта книга уже выдана данному читателю.");

        if (Count("SELECT copies_avail FROM books WHERE id = @id", ("@id", bookId)) <= 0)
            return OpResult.Fail("Нет свободных экземпляров.");

        try
        {
            Database.InTransaction(() =>
            {
                Database.Execute(
                    "INSERT INTO loans(reader_id, book_id, issued_at, due_at) " +
                    "VALUES(@r, @b, date('now','localtime'), date('now','localtime', @mod))",
                    ("@r", readerId), ("@b", bookId), ("@mod", $"+{AppConfig.LoanDays} days"));
                Database.Execute("UPDATE books SET copies_avail = copies_avail - 1 WHERE id = @b", ("@b", bookId));
            });
        }
        catch (Exception ex)
        {
            return OpResult.Fail(ex.Message);
        }

        Audit.Log($"Выдача книги: читатель {readerId}, книга {bookId}");
        return OpResult.Success($"Книга выдана на {AppConfig.LoanDays} дн.");
    }

    public static bool IsReturned(long loanId) =>
        Database.Scalar<string>("SELECT returned_at FROM loans WHERE id = @id", ("@id", loanId)) is not null;

    /// <summary>Возврат книги. Если срок просрочен — штраф начисляется автоматически.</summary>
    public static OpResult Return(long loanId)
    {
        if (!LoanExists(loanId)) return OpResult.Fail("Выдача не найдена.");
        if (IsReturned(loanId)) return OpResult.Fail("Эта книга уже возвращена.");

        long days = Count("SELECT CAST(julianday(date('now','localtime')) - julianday(due_at) AS INTEGER) FROM loans WHERE id = @id",
            ("@id", loanId));
        double amount = 0;

        try
        {
            Database.InTransaction(() =>
            {
                long bookId = Count("SELECT book_id FROM loans WHERE id = @id", ("@id", loanId));
                Database.Execute("UPDATE loans SET returned_at = date('now','localtime') WHERE id = @id", ("@id", loanId));
                Database.Execute("UPDATE books SET copies_avail = copies_avail + 1 WHERE id = @b", ("@b", bookId));
                if (days > 0)
                {
                    amount = days * AppConfig.FinePerDay;
                    Database.Execute("DELETE FROM fines WHERE loan_id = @id AND reason = 'Просрочка' AND paid = 0", ("@id", loanId));
                    if (Count("SELECT COUNT(*) FROM fines WHERE loan_id = @id AND reason = 'Просрочка'", ("@id", loanId)) == 0)
                        Database.Execute("INSERT INTO fines(loan_id, amount, reason) VALUES(@id, @a, 'Просрочка')",
                            ("@id", loanId), ("@a", amount));
                }
            });
        }
        catch (Exception ex)
        {
            return OpResult.Fail(ex.Message);
        }

        Audit.Log($"Возврат книги по выдаче {loanId}");
        return days > 0
            ? OpResult.Success($"Книга принята. Просрочка {days} дн. Начислен штраф: {amount.ToString("F2", CultureInfo.CurrentCulture)} руб.")
            : OpResult.Success("Книга принята.");
    }

    public static OpResult Renew(long loanId)
    {
        if (!LoanExists(loanId)) return OpResult.Fail("Выдача не найдена.");
        if (IsReturned(loanId)) return OpResult.Fail("Книга уже возвращена.");
        if (Count("SELECT date(due_at) < date('now','localtime') FROM loans WHERE id = @id", ("@id", loanId)) == 1)
            return OpResult.Fail("Просроченную книгу продлить нельзя — сначала верните её.");

        Database.Execute("UPDATE loans SET due_at = date(due_at, @mod) WHERE id = @id",
            ("@mod", $"+{AppConfig.RenewDays} days"), ("@id", loanId));
        Audit.Log($"Продление выдачи {loanId} на {AppConfig.RenewDays} дн.");
        return OpResult.Success($"Срок продлён на {AppConfig.RenewDays} дн.");
    }

    /// <summary>
    /// Утеря: выдача закрывается, экземпляр списывается. Штрафы считаются честно:
    /// сначала — просрочка по дням (как при возврате), затем — цена книги.
    /// Если книгу позже найдут, штраф за утерю можно будет снять («Книга нашлась»).
    /// </summary>
    public static OpResult Lost(long loanId)
    {
        if (!LoanExists(loanId)) return OpResult.Fail("Выдача не найдена.");
        if (IsReturned(loanId)) return OpResult.Fail("Книга уже возвращена.");
        try
        {
            Database.InTransaction(() =>
            {
                // 1) Штраф за просрочку на день утери — по той же ставке, что и при возврате
                long days = Count("SELECT CAST(julianday(date('now','localtime')) - julianday(due_at) AS INTEGER) FROM loans WHERE id = @id",
                    ("@id", loanId));
                if (days > 0)
                {
                    Database.Execute("DELETE FROM fines WHERE loan_id = @id AND reason = 'Просрочка' AND paid = 0", ("@id", loanId));
                    if (Count("SELECT COUNT(*) FROM fines WHERE loan_id = @id AND reason = 'Просрочка'", ("@id", loanId)) == 0)
                        Database.Execute("INSERT INTO fines(loan_id, amount, reason) VALUES(@id, @a, 'Просрочка')",
                            ("@id", loanId), ("@a", days * AppConfig.FinePerDay));
                }

                // 2) Закрытие выдачи: экземпляр списывается из фонда, штраф за утерю — по цене книги
                Database.Execute("UPDATE loans SET returned_at = date('now','localtime') WHERE id = @id", ("@id", loanId));
                Database.Execute("UPDATE books SET copies_total = copies_total - 1 WHERE id = (SELECT book_id FROM loans WHERE id = @id)",
                    ("@id", loanId));
                Database.Execute("DELETE FROM fines WHERE loan_id = @id AND reason = 'Утеря' AND paid = 0", ("@id", loanId));
                Database.Execute("INSERT INTO fines(loan_id, amount, reason) " +
                                 "SELECT @id, price, 'Утеря' FROM books WHERE id = (SELECT book_id FROM loans WHERE id = @id)",
                    ("@id", loanId));
            });
        }
        catch (Exception ex)
        {
            return OpResult.Fail(ex.Message);
        }
        Audit.Log($"Утеря книги по выдаче {loanId}");
        return OpResult.Success("Книга списана как утерянная: начислен штраф по цене книги (и за просрочку, если была).");
    }

    /// <summary>
    /// Книга, ранее списанная как утерянная, нашлась: экземпляр возвращается в фонд,
    /// НЕоплаченный штраф за утерю снимается. Штраф за просрочку (за дни до утери) остаётся —
    /// книга всё же находилась на руках дольше срока.
    /// </summary>
    public static OpResult FoundLost(long loanId)
    {
        if (!LoanExists(loanId)) return OpResult.Fail("Выдача не найдена.");
        if (!IsReturned(loanId)) return OpResult.Fail("Эта выдача ещё не закрывалась как утерянная.");
        if (Count("SELECT COUNT(*) FROM fines WHERE loan_id = @id AND reason = 'Утеря'", ("@id", loanId)) == 0)
            return OpResult.Fail("По этой выдаче книга не числится утерянной.");
        if (Count("SELECT COUNT(*) FROM fines WHERE loan_id = @id AND reason = 'Утеря' AND paid = 0", ("@id", loanId)) == 0)
            return OpResult.Fail("Штраф за утерю по этой книге уже оплачен — снимать нечего.");
        try
        {
            Database.InTransaction(() =>
            {
                Database.Execute("DELETE FROM fines WHERE loan_id = @id AND reason = 'Утеря' AND paid = 0", ("@id", loanId));
                Database.Execute("UPDATE books SET copies_total = copies_total + 1 WHERE id = (SELECT book_id FROM loans WHERE id = @id)",
                    ("@id", loanId));
            });
        }
        catch (Exception ex)
        {
            return OpResult.Fail(ex.Message);
        }
        Audit.Log($"Найденная утерянная книга возвращена в фонд (выдача {loanId}), штраф за утерю снят");
        return OpResult.Success("Книга нашлась: экземпляр снова в фонде, штраф за утерю снят.\n" +
                                "Штраф за просрочку (если был) остаётся — книгу держали дольше срока.");
    }

    /// <summary>Оплата всех неоплаченных штрафов читателя (читатель определяется по выбранному штрафу).</summary>
    public static OpResult PayReaderFines(long fineId)
    {
        if (Count("SELECT COUNT(*) FROM fines WHERE id = @id", ("@id", fineId)) == 0) return OpResult.Fail("Штраф не найден.");
        long readerId = Count("SELECT l.reader_id FROM fines f JOIN loans l ON l.id = f.loan_id WHERE f.id = @id", ("@id", fineId));
        long open = Count(
            "SELECT COUNT(*) FROM fines f JOIN loans l ON l.id = f.loan_id WHERE l.reader_id = @r AND f.paid = 0",
            ("@r", readerId));
        if (open == 0) return OpResult.Fail("У этого читателя нет неоплаченных штрафов.");

        Database.Execute(
            "UPDATE fines SET paid = 1 WHERE id IN " +
            "(SELECT f.id FROM fines f JOIN loans l ON l.id = f.loan_id WHERE l.reader_id = @r AND f.paid = 0)",
            ("@r", readerId));
        Audit.Log($"Читатель {readerId}: оплачены все штрафы ({open} шт.)");
        return OpResult.Success($"Долги читателя закрыты: оплачено штрафов — {open}.");
    }

    /// <summary>Пересчёт штрафов для книг, которые просрочены и ещё не возвращены.</summary>
    public static void RecalculateOverdueFines()
    {
        const string days = "CAST(julianday(date('now','localtime')) - julianday({0}) AS INTEGER)";
        Database.InTransaction(() =>
        {
            Database.Execute(
                "INSERT INTO fines(loan_id, amount, reason) " +
                "SELECT l.id, ROUND(" + string.Format(days, "l.due_at") + " * @rate, 2), 'Просрочка' " +
                "FROM loans l WHERE l.returned_at IS NULL AND date(l.due_at) < date('now','localtime') " +
                "AND NOT EXISTS (SELECT 1 FROM fines f WHERE f.loan_id = l.id AND f.reason = 'Просрочка')",
                ("@rate", AppConfig.FinePerDay));
            Database.Execute(
                "UPDATE fines SET amount = ROUND(" + string.Format(days, "(SELECT due_at FROM loans WHERE id = fines.loan_id)") + " * @rate, 2) " +
                "WHERE reason = 'Просрочка' AND paid = 0 AND loan_id IN (SELECT id FROM loans WHERE returned_at IS NULL)",
                ("@rate", AppConfig.FinePerDay));
        });
        Audit.Log("Пересчёт штрафов за просрочку");
    }

    public static OpResult PayFine(long fineId)
    {
        if (Count("SELECT COUNT(*) FROM fines WHERE id = @id", ("@id", fineId)) == 0) return OpResult.Fail("Штраф не найден.");
        if (Count("SELECT paid FROM fines WHERE id = @id", ("@id", fineId)) == 1)
            return OpResult.Fail("Этот штраф уже оплачен.");
        Database.Execute("UPDATE fines SET paid = 1 WHERE id = @id", ("@id", fineId));
        Audit.Log($"Штраф {fineId} оплачен");
        return OpResult.Success("Штраф отмечен как оплаченный.");
    }
}
