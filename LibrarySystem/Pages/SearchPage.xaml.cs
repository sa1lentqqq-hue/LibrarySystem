using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LibrarySystem.Controls;
using LibrarySystem.Data;
using LibrarySystem.Services;
using LibrarySystem.Views;

namespace LibrarySystem.Pages;

/// <summary>Результаты сквозного поиска по книгам и читателям.</summary>
public partial class SearchPage : UserControl
{
    /// <summary>Исходный запрос (главное окно подставляет его в строку поиска при возврате на эту страницу).</summary>
    public string Query { get; }

    public SearchPage(string query)
    {
        InitializeComponent();
        Query = query;
        GridHelper.Setup(ResultGrid);
        Filter.Configure(ResultGrid, "Название", false, new DropFilter("Тип"));   // по умолчанию — по алфавиту; можно оставить только книги или читателей

        DataTable table = Database.Query(
            "SELECT 'Книга' AS [Тип], title AS [Название], author || ' (' || COALESCE(year, '') || ')' AS [Подробности] FROM books " +
            "UNION ALL " +
            "SELECT 'Читатель', full_name, card_no || ', ' || COALESCE(phone, '') FROM readers");
        table.DefaultView.RowFilter = TableFilter.Like(table, query);   // без учёта регистра, в том числе для кириллицы
        DataTable found = table.DefaultView.ToTable();
        Filter.Bind(found);
        ResultGrid.ItemsSource = found.DefaultView;
        InfoText.Text = $"Запрос «{query}» — найдено записей: {found.Rows.Count}";
    }

    /// <summary>Двойной щелчок по результату открывает запись в её разделе (книги или читатели) с готовым фильтром.</summary>
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ResultGrid, (DependencyObject)e.OriginalSource) is not DataGridRow) return;
        if (ResultGrid.SelectedItem is not DataRowView row) return;

        string type = Convert.ToString(row["Тип"]) ?? "";
        string name = Convert.ToString(row["Название"]) ?? "";
        MainWindow.Current.OpenSection(type == "Книга" ? "books" : "readers", name);
    }
}
