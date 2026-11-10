using System.Data;
using System.Text;
using LibrarySystem.Data;
using LibrarySystem.Services;
using Microsoft.Data.Sqlite;

int pass = 0, fail = 0;
void Check(bool ok, string name) { if (ok) pass++; else { fail++; Console.WriteLine("  FAIL: " + name); } }
long N(string sql, params (string, object?)[] a) => Database.Scalar<long>(sql, a);
double D(string sql, params (string, object?)[] a) => Database.Scalar<double>(sql, a);
string S(string sql, params (string, object?)[] a) => Database.Scalar<string>(sql, a) ?? "";
void Invariants(string where)
{
    Check(N("SELECT COUNT(*) FROM books b WHERE copies_avail != copies_total - (SELECT COUNT(*) FROM loans l WHERE l.book_id = b.id AND l.returned_at IS NULL)") == 0, where + ": copies_avail = total - на руках");
    Check(N("SELECT COUNT(*) FROM books WHERE copies_avail < 0") == 0, where + ": нет отрицательных остатков");
    Check(Database.Query("PRAGMA foreign_key_check").Rows.Count == 0, where + ": внешние ключи целы");
}
void Section(string t) => Console.WriteLine("== " + t);

var dir = Path.Combine(Path.GetTempPath(), "dt"); if (Directory.Exists(dir)) Directory.Delete(dir, true);
Directory.CreateDirectory(dir); Environment.SetEnvironmentVariable("XDG_DATA_HOME", dir);

Section("Инициализация БД из schema.sql");
Database.Initialize();
Check(N("SELECT COUNT(*) FROM users") == 3 && N("SELECT COUNT(*) FROM genres") == 8 && N("SELECT COUNT(*) FROM books") == 31 &&
      N("SELECT COUNT(*) FROM readers") == 18 && N("SELECT COUNT(*) FROM loans") == 30 && N("SELECT COUNT(*) FROM fines") == 9, "количество записей по таблицам");
Invariants("после загрузки");

Section("Вход: проверка паролей");
foreach (var (login, pw, role) in new[] { ("admin", "Admin2026", "admin"), ("anna", "User2026", "user"), ("oleg", "User2026", "user") })
{
    var u = Database.Query("SELECT salt, pass_hash, role FROM users WHERE login = @l", ("@l", login)).Rows[0];
    Check(Passwords.Verify(pw, (string)u["salt"], (string)u["pass_hash"]) && (string)u["role"] == role, $"вход {login}");
    Check(!Passwords.Verify("wrong", (string)u["salt"], (string)u["pass_hash"]), $"неверный пароль {login}");
}
Check(Database.Query("SELECT 1 FROM users WHERE login = @l", ("@l", "x' OR '1'='1")).Rows.Count == 0, "SQL-инъекция в логине не проходит");

Section("Выдача книг");
var r = LoanService.Issue(1, 2);
Check(r.Ok, "выдача читателю 1 книги 2: " + r.Message);
Check(N("SELECT copies_avail FROM books WHERE id=2") == 2, "остаток книги 2 уменьшился");
Check(S("SELECT due_at FROM loans ORDER BY id DESC LIMIT 1") == S("SELECT date('now','localtime','+14 days')"), "срок возврата = сегодня + 14 дней");
Check(!LoanService.Issue(1, 2).Ok, "повторная выдача той же книги запрещена");
foreach (var b in new long[] { 3, 4, 5, 6 }) Check(LoanService.Issue(1, b).Ok, $"выдача книги {b} читателю 1");
r = LoanService.Issue(1, 7); Check(!r.Ok && r.Message.Contains("лимит"), "лимит 5 книг на руках: " + r.Message);
Check(!LoanService.Issue(18, 8).Ok, "заблокированный читатель не получает книги");
r = LoanService.Issue(13, 8); Check(!r.Ok && r.Message.Contains("штраф"), "неоплаченный штраф блокирует выдачу (13): " + r.Message);
r = LoanService.Issue(17, 8); Check(!r.Ok, "штраф за утерю блокирует выдачу (17)");
r = LoanService.Issue(12, 8); Check(!r.Ok && r.Message.ToLower().Contains("просроч"), "просроченная книга на руках блокирует выдачу (12): " + r.Message);
Check(LoanService.Issue(2, 14).Ok, "последний экземпляр книги 14 выдан");
r = LoanService.Issue(3, 14); Check(!r.Ok && r.Message.Contains("свободных"), "нет свободных экземпляров: " + r.Message);
Invariants("после выдач");

Section("Возврат и штраф за просрочку");
long id29 = N("SELECT id FROM loans WHERE reader_id=14 AND book_id=28 AND returned_at IS NULL");
long days29 = N("SELECT CAST(julianday(date('now','localtime')) - julianday(due_at) AS INTEGER) FROM loans WHERE id=@i", ("@i", id29));
long avail28 = N("SELECT copies_avail FROM books WHERE id=28");
r = LoanService.Return(id29);
Check(r.Ok && r.Message.Contains("штраф"), "возврат просроченной книги: " + r.Message);
Check(Math.Abs(D("SELECT amount FROM fines WHERE loan_id=@i", ("@i", id29)) - days29 * AppConfig.FinePerDay) < 0.001, $"штраф = {days29} дн. × 0,50");
Check(N("SELECT copies_avail FROM books WHERE id=28") == avail28 + 1, "остаток увеличился после возврата");
Check(!LoanService.Return(id29).Ok, "повторный возврат запрещён");
long id26 = N("SELECT id FROM loans WHERE reader_id=7 AND book_id=24 AND returned_at IS NULL");
LoanService.Return(id26);
Check(N("SELECT COUNT(*) FROM fines WHERE loan_id=@i", ("@i", id26)) == 1, "для выдачи с готовым штрафом при возврате нет дубликата");
long idOk = N("SELECT id FROM loans WHERE reader_id=2 AND book_id=12 AND returned_at IS NULL");
r = LoanService.Return(idOk); Check(r.Ok && !r.Message.Contains("штраф") && N("SELECT COUNT(*) FROM fines WHERE loan_id=@i", ("@i", idOk)) == 0, "возврат в срок — без штрафа");
Check(!LoanService.Return(99999).Ok && !LoanService.Renew(99999).Ok && !LoanService.Lost(99999).Ok && !LoanService.PayFine(99999).Ok, "несуществующие выдача/штраф не проходят молча");
Invariants("после возвратов");

Section("Продление");
long idRenew = N("SELECT id FROM loans WHERE reader_id=3 AND book_id=22 AND returned_at IS NULL");
string due0 = S("SELECT due_at FROM loans WHERE id=@i", ("@i", idRenew));
Check(LoanService.Renew(idRenew).Ok && S("SELECT due_at FROM loans WHERE id=@i", ("@i", idRenew)) == S("SELECT date(@d,'+7 days')", ("@d", due0)), "продление на 7 дней");
long idOverdue = N("SELECT id FROM loans WHERE reader_id=8 AND book_id=1 AND returned_at IS NULL");
Check(!LoanService.Renew(idOverdue).Ok, "просроченную выдачу продлить нельзя");
Check(!LoanService.Renew(idOk).Ok, "возвращённую выдачу продлить нельзя");

Section("Утеря книги");
long idLost = N("SELECT id FROM loans WHERE reader_id=4 AND book_id=7 AND returned_at IS NULL");
long total7 = N("SELECT copies_total FROM books WHERE id=7"); double price7 = D("SELECT price FROM books WHERE id=7");
Check(LoanService.Lost(idLost).Ok, "утеря оформлена");
Check(N("SELECT copies_total FROM books WHERE id=7") == total7 - 1, "экземпляр списан");
Check(Math.Abs(D("SELECT amount FROM fines WHERE loan_id=@i AND reason='Утеря'", ("@i", idLost)) - price7) < 0.001, "штраф за утерю = цена книги");
Check(S("SELECT returned_at FROM loans WHERE id=@i", ("@i", idLost)).Length > 0, "выдача закрыта");
Invariants("после утери");

Section("Книга нашлась (снятие штрафа за утерю)");
Check(LoanService.FoundLost(idLost).Ok, "найденная утерянная книга оформлена");
Check(N("SELECT copies_total FROM books WHERE id=7") == total7, "экземпляр вернулся в фонд");
Check(N("SELECT COUNT(*) FROM fines WHERE loan_id=@i AND reason='Утеря' AND paid=0", ("@i", idLost)) == 0, "неоплаченный штраф за утерю снят");
Invariants("после «книга нашлась»");

Section("Пересчёт штрафов за просрочку");
LoanService.RecalculateOverdueFines();
LoanService.RecalculateOverdueFines();
var overdue = Database.Query("SELECT id, CAST(julianday(date('now','localtime')) - julianday(due_at) AS INTEGER) AS d FROM loans WHERE returned_at IS NULL AND date(due_at) < date('now','localtime')");
Check(overdue.Rows.Count >= 3, $"есть просроченные на руках ({overdue.Rows.Count})");
foreach (DataRow row in overdue.Rows)
{
    long lid = (long)row["id"], d = (long)row["d"];
    Check(N("SELECT COUNT(*) FROM fines WHERE loan_id=@i AND reason='Просрочка'", ("@i", lid)) == 1, $"выдача {lid}: ровно один штраф после двух пересчётов");
    Check(Math.Abs(D("SELECT amount FROM fines WHERE loan_id=@i AND reason='Просрочка'", ("@i", lid)) - d * 0.5) < 0.001, $"выдача {lid}: сумма {d} дн. × 0,50");
}

Section("Оплата штрафов и снятие блокировки");
long fineId = N("SELECT id FROM fines WHERE loan_id=(SELECT id FROM loans WHERE reader_id=13 AND book_id=22 AND returned_at IS NOT NULL LIMIT 1)");
Check(LoanService.PayFine(fineId).Ok, "штраф оплачен");
Check(!LoanService.PayFine(fineId).Ok, "повторная оплата запрещена");
Check(LoanService.Issue(13, 8).Ok, "после оплаты читатель 13 снова может брать книги");

Section("Оплата всех долгов читателя");
long anyFine = N("SELECT id FROM fines WHERE paid = 0 LIMIT 1");
long debtor = N("SELECT l.reader_id FROM fines f JOIN loans l ON l.id = f.loan_id WHERE f.id = @i", ("@i", anyFine));
Check(LoanService.PayReaderFines(anyFine).Ok, "оплата всех долгов читателя выполнена");
Check(N("SELECT COUNT(*) FROM fines f JOIN loans l ON l.id = f.loan_id WHERE l.reader_id = @r AND f.paid = 0", ("@r", debtor)) == 0,
    "неоплаченных штрафов у читателя не осталось");

Invariants("после оплаты");

Section("Транзакции");
long before = N("SELECT COUNT(*) FROM genres");
try { Database.InTransaction(() => { Database.Execute("INSERT INTO genres(name) VALUES('Тест')"); throw new InvalidOperationException("сбой"); }); } catch (InvalidOperationException) { }
Check(N("SELECT COUNT(*) FROM genres") == before, "при сбое транзакция откатывается целиком");
Database.InTransaction(() => Database.Execute("INSERT INTO genres(name) VALUES('Тест')"));
Check(N("SELECT COUNT(*) FROM genres") == before + 1, "успешная транзакция сохраняется");

Section("SQL редактирования (как в окне EditWindow)");
Database.Execute("INSERT INTO books(title, author, genre_id, year, copies_total, price) VALUES(@v0,@v1,@v2,@v3,@v4,@v5)",
    ("@v0", "Новая книга"), ("@v1", "Автор А."), ("@v2", 1L), ("@v3", 2020L), ("@v4", 3L), ("@v5", 12.5));
Database.Execute("UPDATE books SET copies_avail = copies_total - (SELECT COUNT(*) FROM loans WHERE loans.book_id = books.id AND loans.returned_at IS NULL)");
Check(N("SELECT copies_avail FROM books WHERE title='Новая книга'") == 3, "новая книга: доступно = всего");
Database.Execute("INSERT INTO books(title, author, genre_id, year, copies_total, price) VALUES(@a,@b,1,NULL,1,0)", ("@a", "Без года"), ("@b", "Б."));
Database.Execute("UPDATE books SET copies_avail = copies_total - (SELECT COUNT(*) FROM loans WHERE loans.book_id = books.id AND loans.returned_at IS NULL)");
Check(N("SELECT COUNT(*) FROM books WHERE year IS NULL") == 1, "год можно не указывать");
string salt = Passwords.NewSalt();
Database.Execute("INSERT INTO users(login, full_name, role, salt, pass_hash) VALUES(@a,@b,@c,@d,@e)",
    ("@a", "newuser"), ("@b", "Новый Пользователь"), ("@c", "user"), ("@d", salt), ("@e", Passwords.Hash("secret1", salt)));
var nu = Database.Query("SELECT salt, pass_hash FROM users WHERE login='newuser'").Rows[0];
Check(Passwords.Verify("secret1", (string)nu["salt"], (string)nu["pass_hash"]), "новый пользователь входит со своим паролем");
string msg = "";
try { Database.Execute("INSERT INTO users(login, full_name, role, salt, pass_hash) VALUES('admin','x','user','a','b')"); } catch (Exception ex) { msg = ex.Message; }
Check(msg.Contains("UNIQUE"), "повтор логина → UNIQUE: " + msg);
msg = ""; try { Database.Execute("INSERT INTO books(title, author, genre_id, year, copies_total, price) VALUES('x','y',1,5000,1,1)"); } catch (Exception ex) { msg = ex.Message; }
Check(msg.Contains("CHECK"), "год 5000 → CHECK: " + msg);
msg = ""; try { Database.Execute("DELETE FROM genres WHERE id=1"); } catch (Exception ex) { msg = ex.Message; }
Check(msg.Contains("FOREIGN KEY"), "удаление жанра с книгами → FOREIGN KEY: " + msg);
msg = ""; try { Database.Execute("INSERT INTO readers(card_no, full_name, blocked) VALUES('ЧБ-0001','Дубль',0)"); } catch (Exception ex) { msg = ex.Message; }
Check(msg.Contains("UNIQUE"), "повтор номера билета → UNIQUE");
Invariants("после правок");

Section("Отчёты и CSV");
foreach (var rep in ReportService.All)
{
    var t = ReportService.Run(rep);
    Check(t.Columns.Count >= 3, $"отчёт «{rep.Title}»: {t.Rows.Count} строк, {t.Columns.Count} столбцов");
}
Check(ReportService.Run(ReportService.All[0]).Rows.Count == overdue.Rows.Count, "«Должники» = просроченные выдачи на руках");
var top = ReportService.Run(ReportService.All[1]);
Check(top.Rows.Count == 15 && (long)top.Rows[0]["Выдач"] >= (long)top.Rows[14]["Выдач"], "«Популярные» отсортированы по убыванию");
string csv = Path.Combine(dir, "r.csv"); Csv.Write(ReportService.Run(ReportService.All[2]), csv);
var bytes = File.ReadAllBytes(csv);
Check(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "CSV: UTF-8 с BOM (Excel)");
var lines = File.ReadAllLines(csv, Encoding.UTF8);
Check(lines[0].TrimStart('\uFEFF') == "Читатель;Штрафов;Начислено;Не оплачено" && lines.Length > 3, "CSV: заголовок и строки: " + lines[0].TrimStart('\uFEFF'));
var tricky = new DataTable(); tricky.Columns.Add("a"); tricky.Rows.Add("x;y \"q\""); Csv.Write(tricky, csv);
Check(File.ReadAllLines(csv)[1] == "\"x;y \"\"q\"\"\"", "CSV: экранирование ; и кавычек");

Section("Фильтр таблицы (поиск без учёта регистра)");
var bk = Database.Query("SELECT id AS [ID], title AS [Название], author AS [Автор], price AS [Цена] FROM books");
bk.DefaultView.RowFilter = TableFilter.Like(bk, "война"); Check(bk.DefaultView.Count == 1, "«война» находит «Война и мир»");
bk.DefaultView.RowFilter = TableFilter.Like(bk, "ТОЛКИН"); Check(bk.DefaultView.Count == 2, "«ТОЛКИН» — 2 книги");
bk.DefaultView.RowFilter = TableFilter.Like(bk, "12.5"); Check(bk.DefaultView.Count >= 1, "поиск по числу в столбце цены");
foreach (var weird in new[] { "[x", "100%", "o'neil", "a*b", "]", "\\" })
{ try { bk.DefaultView.RowFilter = TableFilter.Like(bk, weird); Check(true, ""); } catch (Exception ex) { Check(false, $"спецсимволы «{weird}»: {ex.Message}"); } }

Section("Резервная копия");
string bak = Database.Backup();
Check(File.Exists(bak) && new FileInfo(bak).Length > 10_000, "файл копии создан: " + Path.GetFileName(bak));
using (var c2 = new SqliteConnection($"Data Source={bak}")) { c2.Open(); using var cmd = c2.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM books"; Check(Convert.ToInt64(cmd.ExecuteScalar()) == N("SELECT COUNT(*) FROM books"), "копия содержит те же книги"); }



Section("SQL и настройки экранов из исходников пользователя");
var cfgs = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "configs.json"))).RootElement;
var cmp = System.Globalization.CompareOptions.IgnoreCase;
foreach (var c in cfgs.EnumerateArray())
{
    string name = c.GetProperty("name").GetString()!, sql = c.GetProperty("sql").GetString()!;
    string? sortCol = c.GetProperty("sort").ValueKind == System.Text.Json.JsonValueKind.Null ? null : c.GetProperty("sort").GetString();
    bool desc = c.GetProperty("desc").GetBoolean();
    DataTable t0;
    try { t0 = Database.Query(sql); } catch (Exception ex) { Check(false, $"[{name}] запрос не выполняется: {ex.Message}"); continue; }
    int total = t0.Rows.Count;
    Check(total > 0, $"[{name}] запрос выполняется: {total} строк, столбцы: {string.Join(", ", t0.Columns.Cast<DataColumn>().Select(x => x.ColumnName))}");

    // сортировка по умолчанию — выражение строится так же, как в FilterBar.Apply
    if (sortCol != null)
    {
        Check(t0.Columns.Contains(sortCol), $"[{name}] столбец сортировки «{sortCol}» существует");
        var view = t0.DefaultView; view.RowFilter = ""; view.Sort = $"[{sortCol}] {(desc ? "DESC" : "ASC")}";
        bool ordered = true;
        for (int i = 1; i < view.Count; i++)
        {
            object a = view[i - 1][sortCol], b = view[i][sortCol];
            int rc = a is string sa && b is string sb ? t0.Locale.CompareInfo.Compare(sa, sb, cmp) : ((IComparable)a).CompareTo(b);
            if (desc ? rc < 0 : rc > 0) ordered = false;
        }
        Check(ordered && view.Count == total, $"[{name}] порядок по «{sortCol}» ({(desc ? "по убыванию" : "по возрастанию")}) верный, строки не потеряны");
        // обратное направление (кнопка ▲/▼)
        view.Sort = $"[{sortCol}] {(desc ? "ASC" : "DESC")}";
        Check(view.Count == total, $"[{name}] обратное направление работает");
    }
    // клик по заголовку любого столбца (FilterBar.OnGridSorting)
    foreach (DataColumn col in t0.Columns)
    {
        try { t0.DefaultView.Sort = $"[{col.ColumnName}] ASC"; var _ = t0.DefaultView.Count; } catch (Exception ex) { Check(false, $"[{name}] сортировка по «{col.ColumnName}»: {ex.Message}"); }
    }
    t0.DefaultView.Sort = "";

    // выпадающие фильтры
    foreach (var d in c.GetProperty("drops").EnumerateArray())
    {
        string col = d.GetProperty("col").GetString()!;
        Check(t0.Columns.Contains(col), $"[{name}] столбец фильтра «{col}» существует");
        List<string> values = d.GetProperty("values").ValueKind == System.Text.Json.JsonValueKind.Null
            ? t0.Rows.Cast<DataRow>().Select(r => Convert.ToString(r[col]) ?? "").Where(s => s.Length > 0).Distinct().OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList()
            : d.GetProperty("values").EnumerateArray().Select(x => x.GetString()!).ToList();
        int sum = 0;
        foreach (var v in values)
        {
            t0.DefaultView.RowFilter = $"[{col}] = '{v.Replace("'", "''")}'";
            sum += t0.DefaultView.Count;
        }
        Check(sum == total, $"[{name}] фильтр «{col}»: {values.Count} значений ({string.Join(" / ", values.Take(4))}…) в сумме дают все {total} строк (получено {sum})");
        // фильтр + поиск вместе
        string like = TableFilter.Like(t0, values[0].Substring(0, Math.Min(3, values[0].Length)));
        t0.DefaultView.RowFilter = $"[{col}] = '{values[0].Replace("'", "''")}' AND ({like})";
        Check(t0.DefaultView.Count >= 1, $"[{name}] фильтр + поиск по тексту вместе");
    }
    t0.DefaultView.RowFilter = "";
}

Section("Подсказки поиска (SuggestService из исходников пользователя)");
SuggestService.Invalidate();
var s1 = SuggestService.Find("вой");
Check(s1.Count >= 1 && s1[0].Text == "Война и мир" && s1[0].Kind == "Книга", "«вой» → «Война и мир»: " + string.Join(" | ", s1.Select(x => x.Kind + ":" + x.Text)));
Check(s1[0].Head == "" && s1[0].Match == "Вой" && s1[0].Tail == "на и мир", "выделение введённых букв: «" + s1[0].Head + "|" + s1[0].Match + "|" + s1[0].Tail + "»");
s1 = SuggestService.Find("КРИ");
Check(s1.Any(x => x.Kind == "Автор" && x.Text == "А. Кристи" && x.Match == "Кри" && x.Head == "А. "), "«кри» → автор «А. Кристи» (по слову, без учёта регистра, выделено «Кри»)");
s1 = SuggestService.Find("иван"); Check(s1.Any(x => x.Kind == "Читатель" && x.Text.StartsWith("Иванов")), "«иван» → читатель «Иванов…»");
s1 = SuggestService.Find("толк"); Check(s1.Count(x => x.Kind == "Автор") == 1, "автор «Толкин» — один раз (без дублей, хотя у него 2 книги)");
s1 = SuggestService.Find("а"); Check(s1.Count <= 8 && s1.Count(x => x.Kind == "Книга") <= 4 && s1.Count(x => x.Kind == "Автор") <= 4 && s1.Count(x => x.Kind == "Читатель") <= 4, $"«а»: не больше 8 в сумме и 4 каждого вида ({s1.Count})");
Check(s1.Take(3).All(x => x.Text.StartsWith("А", StringComparison.OrdinalIgnoreCase)), "сначала идут начинающиеся на введённую букву");
Check(SuggestService.Find("").Count == 0 && SuggestService.Find("   ").Count == 0 && SuggestService.Find("яяяя").Count == 0, "пустой и несовпадающий запрос — без подсказок");
Database.Execute("INSERT INTO books(title, author, genre_id, year, copies_total, price) VALUES('Яблоко раздора','Я. Новый',1,2024,1,5)");
Check(SuggestService.Find("ябл").Count == 0, "до сброса кэша новая книга ещё не видна (кэш)");
SuggestService.Invalidate();
Check(SuggestService.Find("ябл").Any(x => x.Text == "Яблоко раздора"), "после Invalidate() новая книга появляется в подсказках");
Check(SuggestService.Find("я. н").Any(x => x.Kind == "Автор"), "подсказка по автору с пробелом в запросе");



Section("Обновление схемы: версия, триггеры, представления");
Check(N("PRAGMA user_version") == 2, "PRAGMA user_version = 2 после запуска");
foreach (var obj in new[] { "trg_loans_before_insert", "trg_loans_before_return", "trg_books_history", "v_overdue_loans", "v_book_popularity", "v_reader_fines", "v_genre_loans", "v_books_consistency", "books_history" })
    Check(N("SELECT COUNT(*) FROM sqlite_master WHERE name = @n", ("@n", obj)) == 1, "объект схемы создан: " + obj);

void Expect(string sql, string part, string label)
{
    string m = ""; try { Database.Execute(sql); } catch (Exception ex) { m = ex.Message; }
    Check(m.Contains(part), $"БД сама запрещает: {label} (получено: «{m}»)");
}
string today = "date('now','localtime')";
string Ins(long reader, long book, string issued, string due) => $"INSERT INTO loans(reader_id, book_id, issued_at, due_at) VALUES({reader}, {book}, {issued}, {due})";
Database.Execute("INSERT INTO readers(card_no, full_name, blocked) VALUES('Т-1','Тест Один',0),('Т-2','Тест Два',0),('Т-3','Тест Три',1),('Т-4','Тест Четыре',0)");
long rA = N("SELECT id FROM readers WHERE card_no='Т-1'"), rB = N("SELECT id FROM readers WHERE card_no='Т-2'"), rBlocked = N("SELECT id FROM readers WHERE card_no='Т-3'"), rC = N("SELECT id FROM readers WHERE card_no='Т-4'");
Database.Execute("INSERT INTO books(title, author, genre_id, year, copies_total, price) VALUES('Тестовая А','Т.',1,2000,1,10),('Тестовая Б','Т.',1,2000,9,10)");
Database.Execute("UPDATE books SET copies_avail = copies_total - (SELECT COUNT(*) FROM loans WHERE loans.book_id = books.id AND loans.returned_at IS NULL)");
long bookOne = N("SELECT id FROM books WHERE title='Тестовая А'"), bookMany = N("SELECT id FROM books WHERE title='Тестовая Б'");
Expect(Ins(rA, bookOne, today, "date('now','localtime','-1 day')"), "раньше даты выдачи", "срок возврата раньше выдачи");
Expect(Ins(rBlocked, bookMany, today, "date('now','localtime','+14 days')"), "заблокирован", "выдача заблокированному читателю");
Database.Execute(Ins(rA, bookOne, today, "date('now','localtime','+14 days')"));      // прямой INSERT без программы — допустим, правила соблюдены
Check(N("SELECT COUNT(*) FROM loans WHERE reader_id=@r", ("@r", rA)) == 1, "прямая корректная выдача проходит");
Expect(Ins(rA, bookMany, today, "date('now','localtime','+14 days')").Replace("VALUES", "VALUES"), "", "—");                    // 2-я книга читателю A (разрешено)
Database.Execute("UPDATE books SET copies_avail = 0 WHERE id=@b", ("@b", bookOne));
Expect(Ins(rB, bookOne, today, "date('now','localtime','+14 days')"), "свободных", "нет свободных экземпляров");
Database.Execute("UPDATE books SET copies_avail = 1 WHERE id=@b", ("@b", bookOne));
Expect(Ins(rA, bookOne, today, "date('now','localtime','+14 days')"), "уже выдана", "повторная выдача той же книги");
for (int i = 0; i < 3; i++) Database.Execute(Ins(rA, N("SELECT id FROM books WHERE copies_avail > 2 AND id NOT IN (SELECT book_id FROM loans WHERE reader_id=@r AND returned_at IS NULL) ORDER BY id LIMIT 1", ("@r", rA)), today, "date('now','localtime','+14 days')"));
Check(N("SELECT COUNT(*) FROM loans WHERE reader_id=@r AND returned_at IS NULL", ("@r", rA)) == 5, "у читателя A на руках 5 книг");
Expect(Ins(rA, N("SELECT id FROM books WHERE copies_avail > 2 AND id NOT IN (SELECT book_id FROM loans WHERE reader_id=@r AND returned_at IS NULL) ORDER BY id LIMIT 1", ("@r", rA)), today, "date('now','localtime','+14 days')"), "лимит", "6-я книга на руках");
Database.Execute(Ins(rC, bookMany, today, "date('now','localtime','+14 days')"));
long loanC = N("SELECT MAX(id) FROM loans");
Database.Execute("INSERT INTO fines(loan_id, amount, reason, paid) VALUES(@l, 5, 'Тест', 0)", ("@l", loanC));
Expect(Ins(rC, N("SELECT id FROM books WHERE copies_avail > 2 AND id != @b ORDER BY id LIMIT 1", ("@b", bookMany)), today, "date('now','localtime','+14 days')"), "неоплаченные", "неоплаченный штраф");
Database.Execute("UPDATE fines SET paid = 1 WHERE loan_id=@l", ("@l", loanC));
Database.Execute("UPDATE loans SET issued_at = date('now','localtime','-30 days'), due_at = date('now','localtime','-16 days') WHERE id=@l", ("@l", loanC));
Expect(Ins(rC, N("SELECT id FROM books WHERE copies_avail > 2 AND id != @b ORDER BY id LIMIT 1", ("@b", bookMany)), today, "date('now','localtime','+14 days')"), "просроченные", "просроченная книга на руках");

Section("Триггер возврата");
long anyActive = N("SELECT id FROM loans WHERE returned_at IS NULL AND reader_id=@r LIMIT 1", ("@r", rA));
Expect($"UPDATE loans SET returned_at = '2000-01-01' WHERE id = {anyActive}", "раньше даты выдачи", "возврат «задним числом» до выдачи");
long anyReturned = N("SELECT id FROM loans WHERE returned_at IS NOT NULL LIMIT 1");
Expect($"UPDATE loans SET returned_at = {today} WHERE id = {anyReturned}", "уже возвращена", "повторный возврат");

Section("История изменений книг (триггер)");
long hBefore = N("SELECT COUNT(*) FROM books_history");
Database.Execute("UPDATE books SET price = price WHERE id=@b", ("@b", bookMany));
Check(N("SELECT COUNT(*) FROM books_history") == hBefore, "цена не изменилась — записи в истории нет");
Database.Execute("UPDATE books SET copies_avail = copies_avail WHERE id=@b", ("@b", bookMany));
Check(N("SELECT COUNT(*) FROM books_history") == hBefore, "изменение остатка «доступно» не попадает в историю");
Database.Execute("UPDATE books SET price = 12.5 WHERE id=@b", ("@b", bookMany));
var hrow = Database.Query("SELECT old_price, new_price, old_total, new_total FROM books_history WHERE book_id=@b ORDER BY id DESC LIMIT 1", ("@b", bookMany)).Rows[0];
Check(N("SELECT COUNT(*) FROM books_history") == hBefore + 1 && Convert.ToDouble(hrow["old_price"]) == 10 && Convert.ToDouble(hrow["new_price"]) == 12.5, "смена цены 10 → 12,5 записана в историю");
Database.Execute("UPDATE books SET copies_total = 10 WHERE id=@b", ("@b", bookMany));
Check(N("SELECT new_total FROM books_history WHERE book_id=@b ORDER BY id DESC LIMIT 1", ("@b", bookMany)) == 10, "смена количества экземпляров записана");
Database.Execute("UPDATE books SET copies_avail = copies_total - (SELECT COUNT(*) FROM loans WHERE loans.book_id = books.id AND loans.returned_at IS NULL)");

Section("Представления согласованы с данными");
Check(N("SELECT COUNT(*) FROM v_overdue_loans") == N("SELECT COUNT(*) FROM loans WHERE returned_at IS NULL AND date(due_at) < date('now','localtime')"), "v_overdue_loans = просроченные выдачи на руках");
Check(N("SELECT SUM(loans_count) FROM v_book_popularity") == N("SELECT COUNT(*) FROM loans"), "v_book_popularity: сумма выдач = число выдач");
Check(Math.Abs(D("SELECT SUM(total) FROM v_reader_fines") - D("SELECT SUM(amount) FROM fines")) < 0.001, "v_reader_fines: сумма = сумма всех штрафов");
Check(N("SELECT SUM(loans_count) FROM v_genre_loans") == N("SELECT COUNT(*) FROM loans"), "v_genre_loans: сумма по жанрам = число выдач");
Check(N("SELECT COUNT(*) FROM v_books_consistency") == 0, "v_books_consistency пуст после всех операций");
Database.Execute("UPDATE books SET copies_avail = copies_avail + 1 WHERE id=@b", ("@b", bookMany));
Check(N("SELECT COUNT(*) FROM v_books_consistency") == 1, "рассогласованный остаток книги сразу виден в диагностике");
Database.Execute("UPDATE books SET copies_avail = copies_avail - 1 WHERE id=@b", ("@b", bookMany));
Check(N("SELECT COUNT(*) FROM v_books_consistency") == 0, "после исправления диагностика снова пуста");
Check(ReportService.Run(ReportService.All[3]).Rows.Count >= 2 && ReportService.Run(ReportService.All[4]).Rows.Count == 0, "отчёты «История изменений» и «Проверка остатков» работают");
Check(ReportService.All.Count == 5, "в программе 5 отчётов");

Section("Приложение работает поверх триггеров (все сценарии выдачи/возврата)");
Check(LoanService.Return(anyActive).Ok, "возврат через программу при включённых триггерах");
Check(LoanService.Issue(rB, bookMany).Ok, "выдача через программу при включённых триггерах");
Invariants("с триггерами");

Section("Обновление СТАРОЙ базы (как у пользователя) и повторный запуск");
string dbPath = Database.DbPath;
File.Delete(dbPath);
using (var old = new SqliteConnection($"Data Source={dbPath};Foreign Keys=True"))
{
    old.Open();
    using var cmd = old.CreateCommand(); cmd.CommandText = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "schema.sql")); cmd.ExecuteNonQuery();
}
Database.Initialize();
Check(N("PRAGMA user_version") == 2 && N("SELECT COUNT(*) FROM sqlite_master WHERE type='trigger'") == 3 && N("SELECT COUNT(*) FROM sqlite_master WHERE type='view'") == 5, "старая база (версия 0) обновлена до версии 2: 3 триггера, 5 представлений");
Check(N("SELECT COUNT(*) FROM books") == 31 && N("SELECT COUNT(*) FROM loans") == 30, "данные старой базы сохранились");
Database.Initialize();
Check(N("PRAGMA user_version") == 2 && N("SELECT COUNT(*) FROM sqlite_master WHERE type='trigger'") == 3, "повторный запуск ничего не дублирует и не ломает");
Check(LoanService.Issue(1, 2).Ok, "после обновления выдача работает");
Invariants("после обновления");


Section("Главная: данные для плашки и диаграмм (DashboardService)");
var (odLoans, odReaders) = DashboardService.GetOverdue();
Check(odLoans == N("SELECT COUNT(*) FROM loans WHERE returned_at IS NULL AND date(due_at) < date('now','localtime')"), $"просрочено выдач: {odLoans}");
Check(odReaders == N("SELECT COUNT(DISTINCT reader_id) FROM loans WHERE returned_at IS NULL AND date(due_at) < date('now','localtime')") && odReaders <= odLoans, $"читателей с просрочкой: {odReaders}");
var genres = DashboardService.GetGenreLoans();
Check(genres.Count == N("SELECT COUNT(*) FROM genres") && genres.Sum(g => g.Count) == N("SELECT COUNT(*) FROM loans"), "выдачи по жанрам: все жанры, сумма = число выдач");
Check(genres.Zip(genres.Skip(1), (a, b) => a.Count >= b.Count).All(x => x), "жанры упорядочены по убыванию выдач");
var weeks13 = DashboardService.GetWeeklyLoans(13);
Check(weeks13.Count == 13 && weeks13.Sum(w => w.Count) == N("SELECT COUNT(*) FROM loans WHERE issued_at >= date('now','localtime','-90 days')"), $"13 недель в сумме = выдачи за 91 день ({weeks13.Sum(w => w.Count)})");
var weeks8 = DashboardService.GetWeeklyLoans(8);
Check(weeks8.Count == 8 && weeks8[^1].Start == DateTime.Today.AddDays(-6) && weeks8[0].Start < weeks8[^1].Start, "8 недель: последняя заканчивается сегодня, порядок от старых к новым");
Check(weeks8.Sum(w => w.Count) == N("SELECT COUNT(*) FROM loans WHERE issued_at >= date('now','localtime','-55 days')"), "8 недель = выдачи за 56 дней");
Check(weeks8.Zip(weeks8.Skip(1), (a, b) => (b.Start - a.Start).Days == 7).All(x => x), "недели идут без пропусков и пересечений");
Console.WriteLine($"\nИТОГО: проверок пройдено {pass}, провалено {fail}");
return fail == 0 ? 0 : 1;
