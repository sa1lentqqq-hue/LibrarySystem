using System.Windows;
using System.Windows.Controls;
using LibrarySystem.Controls;
using LibrarySystem.Data;
using LibrarySystem.Services;

namespace LibrarySystem.Pages;

/// <summary>«О программе»: сведения об авторе и статистика базы данных.</summary>
public partial class AboutPage : UserControl, IRefreshable
{
    public AboutPage()
    {
        InitializeComponent();
        GridHelper.Setup(StatsGrid);
        VersionText.Text = $"Версия {AppConfig.Version}";
        AuthorText.Text = $"Автор: {AppConfig.AuthorName}";
        GroupText.Text = $"Группа: {AppConfig.Group}";
        Reload();
    }

    private void Reload() => StatsGrid.ItemsSource = Database.Query(
        @"SELECT 'Пользователи' AS [Таблица], COUNT(*) AS [Записей] FROM users UNION ALL
          SELECT 'Жанры', COUNT(*) FROM genres UNION ALL SELECT 'Книги', COUNT(*) FROM books UNION ALL
          SELECT 'Читатели', COUNT(*) FROM readers UNION ALL SELECT 'Выдачи', COUNT(*) FROM loans UNION ALL
          SELECT 'Штрафы', COUNT(*) FROM fines UNION ALL SELECT 'Журнал действий', COUNT(*) FROM action_log").DefaultView;

    public void Refresh() => Reload();

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText($"Библиотечная система v{AppConfig.Version}\nАвтор: {AppConfig.AuthorName}, группа {AppConfig.Group}\nC#, .NET 8, WPF, SQLite");
        Ui.Success("Сведения скопированы в буфер обмена.");
    }
}
