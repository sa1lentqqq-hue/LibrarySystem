using System.Data;
using LibrarySystem.Data;

namespace LibrarySystem.Services;

public sealed record ReportDef(string Title, string Sql);

/// <summary>
/// Отчёты. Запросы строятся на представлениях (VIEW) базы данных — они объявлены в Data/upgrade_v2.sql,
/// поэтому в программе остаются короткие и понятные SELECT. Вывод в файл выполняет класс Csv.
/// </summary>
public static class ReportService
{
    public static IReadOnlyList<ReportDef> All { get; } = new[]
    {
        new ReportDef("Должники и просрочки",
            @"SELECT card_no AS [Билет], full_name AS [Читатель], phone AS [Телефон], title AS [Книга],
                     due_at AS [Срок возврата], days_overdue AS [Дней просрочки]
              FROM v_overdue_loans
              ORDER BY days_overdue DESC"),

        new ReportDef("Популярные книги (топ-15)",
            @"SELECT title AS [Название], author AS [Автор], genre AS [Жанр], loans_count AS [Выдач]
              FROM v_book_popularity
              ORDER BY loans_count DESC, title LIMIT 15"),

        new ReportDef("Штрафы по читателям",
            @"SELECT full_name AS [Читатель], fines_count AS [Штрафов], total AS [Начислено], unpaid AS [Не оплачено]
              FROM v_reader_fines
              ORDER BY unpaid DESC"),

        new ReportDef("История изменений книг (цена и количество)",
            @"SELECT b.title AS [Книга], h.changed_at AS [Когда],
                     h.old_price AS [Цена была], h.new_price AS [Цена стала],
                     h.old_total AS [Всего было], h.new_total AS [Всего стало]
              FROM books_history h JOIN books b ON b.id = h.book_id
              ORDER BY h.id DESC"),

        new ReportDef("Проверка остатков (должен быть пуст)",
            @"SELECT title AS [Книга], copies_total AS [Всего], copies_avail AS [Доступно], on_hand AS [На руках]
              FROM v_books_consistency"),
    };

    public static DataTable Run(ReportDef report)
    {
        Audit.Log("Сформирован отчёт: " + report.Title);
        return Database.Query(report.Sql);
    }
}
