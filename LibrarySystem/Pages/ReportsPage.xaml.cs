using System.Data;
using System.Windows;
using System.Windows.Controls;
using LibrarySystem.Controls;
using LibrarySystem.Data;
using LibrarySystem.Services;

namespace LibrarySystem.Pages;

/// <summary>Отчёты (просмотр и вывод в CSV) и резервная копия базы данных (администратор).</summary>
public partial class ReportsPage : UserControl, IRefreshable
{
    private DataTable _data = new();
    private ReportDef? _shown;

    public ReportsPage()
    {
        InitializeComponent();
        GridHelper.Setup(ReportGrid);
        Filter.Configure(ReportGrid);      // порядок строк — как в самом отчёте, пока пользователь не выберет сортировку
        BackupButton.Visibility = Session.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        ReportBox.ItemsSource = ReportService.All;
        ReportBox.DisplayMemberPath = nameof(ReportDef.Title);
        ReportBox.SelectedIndex = 0;
        OnBuild(this, new RoutedEventArgs());
    }

    public void Refresh() => OnBuild(this, new RoutedEventArgs());

    private void OnBuild(object sender, RoutedEventArgs e)
    {
        if (ReportBox.SelectedItem is not ReportDef report) return;
        _data = ReportService.Run(report);
        if (!ReferenceEquals(_shown, report)) Filter.Reset();    // другой отчёт — другие столбцы: поиск и сортировку сбрасываем
        _shown = report;
        Filter.Bind(_data);
        ReportGrid.ItemsSource = _data.DefaultView;
        InfoText.Text = $"Строк в отчёте: {_data.Rows.Count}";
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_data.Rows.Count == 0)
        {
            Ui.Warn("Сначала сформируйте отчёт — в нём должны быть данные.");
            return;
        }
        Ui.SaveCsv(_data.DefaultView.ToTable(), $"report_{ReportBox.SelectedIndex + 1}");    // с учётом поиска и сортировки
    }

    private void OnExportWord(object sender, RoutedEventArgs e)
    {
        if (_data.Rows.Count == 0)
        {
            Ui.Warn("Сначала сформируйте отчёт — в нём должны быть данные.");
            return;
        }
        string title = ReportBox.SelectedItem is ReportDef report ? report.Title : "Отчёт";
        Ui.SaveWord(_data.DefaultView.ToTable(), $"report_{ReportBox.SelectedIndex + 1}", title);   // с учётом поиска и сортировки
    }

    private void OnBackup(object sender, RoutedEventArgs e)
    {
        if (!Session.IsAdmin) { Ui.Error("Резервную копию может создать только администратор."); return; }
        try
        {
            string file = Database.Backup();
            Audit.Log("Резервная копия БД: " + System.IO.Path.GetFileName(file));
            Ui.Success("Резервная копия создана:\n" + file);
        }
        catch (Exception ex)
        {
            Ui.Error("Не удалось создать копию: " + ex.Message);
        }
    }
}
