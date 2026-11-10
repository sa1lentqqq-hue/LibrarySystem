using LibrarySystem.Controls;
using LibrarySystem.Data;
using LibrarySystem.Views;

namespace LibrarySystem.Pages;

/// <summary>Какие разделы есть в программе и кому они доступны. Права проверяются здесь, а не только скрытием кнопок.</summary>
public static class PageFactory
{
    public static Screen? Create(string key, string? filter = null)
    {
        bool admin = Session.IsAdmin;
        if (key is "genres" or "users" or "log" && !admin) return null;

        switch (key)
        {
            case "home": return new Screen(new HomePage(), "Главное меню", "Главная");
            case "books":
                return Table("Фонд книг", filter, new TableConfig("books",
                    @"SELECT b.id AS [ID], b.title AS [Название], b.author AS [Автор], g.name AS [Жанр], b.year AS [Год],
                             b.copies_total AS [Всего], b.copies_avail AS [Доступно], b.price AS [Цена]
                      FROM books b JOIN genres g ON g.id = b.genre_id ORDER BY b.title",
                    new[]
                    {
                        new FieldDef("title", "Название", FieldKind.Text, true),
                        new FieldDef("author", "Автор", FieldKind.Text, true),
                        new FieldDef("genre_id", "Жанр", FieldKind.Lookup, true) { LookupSql = "SELECT id, name FROM genres ORDER BY name" },
                        new FieldDef("year", "Год издания", FieldKind.Integer),
                        new FieldDef("copies_total", "Всего экземпляров", FieldKind.Integer, true),
                        new FieldDef("price", "Цена, руб.", FieldKind.Real, true),
                    }, CanAdd: true, CanEdit: true, CanDelete: admin,
                    DefaultSort: "Название", Filters: new[] { new DropFilter("Жанр") }));

            case "readers":
                return Table("Читатели", filter, new TableConfig("readers",
                    @"SELECT id AS [ID], card_no AS [Билет], full_name AS [ФИО], phone AS [Телефон], birth_date AS [Дата рождения],
                             registered_at AS [Регистрация], CASE blocked WHEN 1 THEN 'Заблокирован' ELSE 'Активен' END AS [Статус]
                      FROM readers ORDER BY full_name",
                    new[]
                    {
                        new FieldDef("card_no", "Номер билета", FieldKind.Text, true),
                        new FieldDef("full_name", "ФИО", FieldKind.Text, true),
                        new FieldDef("phone", "Телефон", FieldKind.Text),
                        new FieldDef("birth_date", "Дата рождения", FieldKind.Date, true),
                        new FieldDef("blocked", "Статус", FieldKind.Lookup, true)
                            { LookupSql = "SELECT 0 AS id, 'Активен' AS name UNION ALL SELECT 1, 'Заблокирован'" },
                    }, CanAdd: true, CanEdit: true, CanDelete: admin,
                    DefaultSort: "ФИО", Filters: new[] { new DropFilter("Статус") }));

            case "genres":
                return Table("Справочник жанров", filter, new TableConfig("genres",
                    "SELECT id AS [ID], name AS [Жанр] FROM genres ORDER BY name",
                    new[] { new FieldDef("name", "Название жанра", FieldKind.Text, true) },
                    CanAdd: true, CanEdit: true, CanDelete: true, DefaultSort: "Жанр"));

            case "users":
                return Table("Пользователи", filter, new TableConfig("users",
                    "SELECT id AS [ID], login AS [Логин], full_name AS [ФИО], role AS [Роль], created_at AS [Создан] FROM users ORDER BY id",
                    new[]
                    {
                        new FieldDef("login", "Логин", FieldKind.Text, true),
                        new FieldDef("full_name", "ФИО", FieldKind.Text, true),
                        new FieldDef("role", "Роль", FieldKind.Choice, true) { Choices = new[] { "user", "admin" } },
                        new FieldDef("pass_hash", "Пароль (пусто = не менять)", FieldKind.Password),
                    }, CanAdd: true, CanEdit: true, CanDelete: true,
                    DefaultSort: "Логин", Filters: new[] { new DropFilter("Роль") }));

            case "log":
                return Table("Журнал действий", filter, new TableConfig("action_log",
                    @"SELECT a.id AS [ID], a.created_at AS [Время], COALESCE(u.login, '—') AS [Пользователь], a.action AS [Действие]
                      FROM action_log a LEFT JOIN users u ON u.id = a.user_id ORDER BY a.id DESC",
                    Array.Empty<FieldDef>(), CanAdd: false, CanEdit: false, CanDelete: false,
                    DefaultSort: "ID", DefaultDescending: true, Filters: new[] { new DropFilter("Пользователь") }));

            // @@CASES
            case "loans":   return new Screen(new LoansPage(filter), "Выдача и возврат");
            case "fines":   return new Screen(new FinesPage(filter), "Штрафы");
            case "reports": return new Screen(new ReportsPage(), "Отчёты");
            case "about":   return new Screen(new AboutPage(), "О программе");
            default: return null;
        }
    }

    private static Screen Table(string title, string? filter, TableConfig config) =>
        new(new TablePage(title, config, filter), title);
}
