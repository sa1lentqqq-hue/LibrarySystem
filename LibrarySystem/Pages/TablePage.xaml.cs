using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using LibrarySystem.Controls;
using LibrarySystem.Data;
using LibrarySystem.Services;
using LibrarySystem.Views;

namespace LibrarySystem.Pages;

/// <summary>
/// Универсальная страница «таблица + действия»: фонд, читатели, жанры, пользователи, журнал.
/// Панель фильтров (поиск с «✕», фильтр по столбцу, сортировка; по умолчанию — по алфавиту/номеру), сортировка кликом по заголовку, добавление, правка, удаление, вывод в CSV.
/// </summary>
public partial class TablePage : UserControl, IRefreshable
{
    private readonly string _caption;
    private readonly TableConfig _cfg;
    private DataTable _data = new();

    public TablePage(string caption, TableConfig cfg, string? initialFilter = null)
    {
        InitializeComponent();
        _caption = caption;
        _cfg = cfg;
        GridHelper.Setup(DataGridMain);

        if (cfg.CanAdd) AddButton("Добавить", "BrassButton", OnAdd);
        if (cfg.CanEdit) AddButton("Изменить", "WoodButton", OnEdit);
        if (cfg.CanDelete) AddButton("Удалить", "DangerButton", OnDelete);
        AddButton("В CSV", "SuccessButton", OnExport);
        AddButton("В Word", "WoodButton", OnExportWord);

        Filter.Configure(DataGridMain, cfg.DefaultSort, cfg.DefaultDescending, (cfg.Filters ?? Array.Empty<DropFilter>()).ToArray());
        if (!string.IsNullOrEmpty(initialFilter)) Filter.SetSearch(initialFilter);   // например, запись, найденная через поиск
        Reload();
    }

    private void AddButton(string text, string styleKey, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Style = (Style)FindResource(styleKey), Margin = new Thickness(0, 0, 8, 0), MinWidth = 110 };
        button.Click += handler;
        ButtonsPanel.Children.Add(button);
    }

    private void Reload()
    {
        _data = Database.Query(_cfg.ListSql);
        Filter.Bind(_data);                              // фильтр и сортировка применяются до показа — таблица рисуется один раз
        DataGridMain.ItemsSource = _data.DefaultView;
    }

    public void Refresh() => Reload();

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var dialog = new EditWindow("Добавление: " + _caption, _cfg.Table, _cfg.Fields, null) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) Reload();
    }

    private void OnEdit(object sender, RoutedEventArgs e)
    {
        long? id = GridHelper.SelectedId(DataGridMain);
        if (id is null) { Ui.Warn("Выберите запись в таблице."); return; }
        var dialog = new EditWindow("Изменение: " + _caption, _cfg.Table, _cfg.Fields, id) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) Reload();
    }

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // двойной щелчок по строке (не по заголовку) открывает редактирование
        if (_cfg.CanEdit && ItemsControl.ContainerFromElement(DataGridMain, (DependencyObject)e.OriginalSource) is DataGridRow)
            OnEdit(sender, e);
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        long? id = GridHelper.SelectedId(DataGridMain);
        if (id is null) { Ui.Warn("Выберите запись в таблице."); return; }
        if (_cfg.Table == "users" && id == Session.UserId) { Ui.Error("Нельзя удалить пользователя, под которым вы вошли."); return; }
        if (!Ui.Confirm("Удалить выбранную запись?\nЭто действие нельзя отменить.")) return;

        try
        {
            Database.Execute($"DELETE FROM {_cfg.Table} WHERE id = @id", ("@id", id));
            Audit.Log($"Удаление записи id={id} из таблицы {_cfg.Table}");
            Reload();
        }
        catch (Exception ex)
        {
            Ui.Error(ex.Message.Contains("FOREIGN KEY")
                ? "Запись используется в других таблицах — удаление невозможно."
                : ex.Message);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e) => Ui.SaveCsv(_data.DefaultView.ToTable(), _cfg.Table);

    private void OnExportWord(object sender, RoutedEventArgs e) => Ui.SaveWord(_data.DefaultView.ToTable(), _cfg.Table, _caption);
}
