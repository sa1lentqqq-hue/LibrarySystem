using System.Data;
using System.Windows;
using System.Windows.Controls;
using LibrarySystem.Controls;
using LibrarySystem.Data;
using LibrarySystem.Services;

namespace LibrarySystem.Pages;

/// <summary>Выдача и возврат книг. Все правила (лимиты, штрафы, блокировка) — в LoanService.</summary>
public partial class LoansPage : UserControl, IRefreshable
{
    private DataTable _data = new();

    public LoansPage(string? initialStatus = null)
    {
        InitializeComponent();
        GridHelper.Setup(LoansGrid);
        // по умолчанию — свежие выдачи сверху (ID по убыванию); фильтр по статусу из фиксированного списка
        Filter.Configure(LoansGrid, "ID", true, new DropFilter("Статус", new[] { "На руках", "Просрочена", "Возвращена" }));
        if (!string.IsNullOrEmpty(initialStatus)) Filter.SelectDrop("Статус", initialStatus);   // например, переход с плашки «Просрочено»
        Reload();
    }

    private void LoadCombos()
    {
        DataTable readers = Database.Query("SELECT id, card_no || ' — ' || full_name AS name FROM readers WHERE blocked = 0 ORDER BY full_name");
        ReaderBox.ItemsSource = readers.DefaultView;
        ReaderBox.SelectedValuePath = "id";
        ReaderBox.DisplayMemberPath = "name";

        DataTable books = Database.Query("SELECT id, title || ' — ' || author || ' (' || copies_avail || ' шт.)' AS name FROM books WHERE copies_avail > 0 ORDER BY title");
        BookBox.ItemsSource = books.DefaultView;
        BookBox.SelectedValuePath = "id";
        BookBox.DisplayMemberPath = "name";
    }

    private void Reload()
    {
        _data = Database.Query(
            @"SELECT l.id AS [ID], r.full_name AS [Читатель], b.title AS [Книга], l.issued_at AS [Выдана],
                     l.due_at AS [Срок возврата], COALESCE(l.returned_at, '') AS [Возвращена],
                     CASE WHEN l.returned_at IS NOT NULL THEN 'Возвращена'
                          WHEN date(l.due_at) < date('now','localtime') THEN 'Просрочена'
                          ELSE 'На руках' END AS [Статус]
              FROM loans l JOIN readers r ON r.id = l.reader_id JOIN books b ON b.id = l.book_id
              ORDER BY l.id DESC");
        Filter.Bind(_data);
        LoansGrid.ItemsSource = _data.DefaultView;
        LoadCombos();
    }

    public void Refresh() => Reload();

    private void Show(OpResult result)
    {
        if (result.Ok) Ui.Success(result.Message); else Ui.Fail(result.Message);
        Reload();
    }

    private long? SelectedLoan()
    {
        long? id = GridHelper.SelectedId(LoansGrid);
        if (id is null) Ui.Warn("Выберите выдачу в таблице.");
        return id;
    }

    private void OnIssue(object sender, RoutedEventArgs e)
    {
        if (ReaderBox.SelectedValue is null || BookBox.SelectedValue is null)
        {
            Ui.Warn("Выберите читателя и книгу.");
            return;
        }
        Show(LoanService.Issue(Convert.ToInt64(ReaderBox.SelectedValue), Convert.ToInt64(BookBox.SelectedValue)));
    }

    private void OnReturn(object sender, RoutedEventArgs e)
    {
        if (SelectedLoan() is long id) Show(LoanService.Return(id));
    }

    private void OnRenew(object sender, RoutedEventArgs e)
    {
        if (SelectedLoan() is long id) Show(LoanService.Renew(id));
    }

    private void OnLost(object sender, RoutedEventArgs e)
    {
        if (SelectedLoan() is not long id) return;
        if (!Ui.Confirm("Отметить книгу как утерянную?\nЭкземпляр будет списан, читателю начислится штраф:\n" +
                        "просрочка по дням + цена книги. Если книга найдётся, штраф за утерю можно будет снять."))
            return;
        Show(LoanService.Lost(id));
    }

    private void OnFound(object sender, RoutedEventArgs e)
    {
        if (SelectedLoan() is not long id) return;
        if (!Ui.Confirm("Ранее утерянная книга нашлась?\nЭкземпляр вернётся в фонд, штраф за утерю снимется.\n" +
                        "Штраф за просрочку (если был) останется."))
            return;
        Show(LoanService.FoundLost(id));
    }
}
