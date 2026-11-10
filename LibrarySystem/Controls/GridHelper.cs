using System.Data;
using System.Windows.Controls;
using System.Windows.Data;

namespace LibrarySystem.Controls;

/// <summary>Общая настройка таблиц: форматирование чисел, ширина столбца ID, получение выбранной записи.</summary>
public static class GridHelper
{
    public static void Setup(DataGrid grid)
    {
        grid.AutoGeneratingColumn += OnAutoGeneratingColumn;

        // Виртуализация: рисуются только видимые строки и столбцы, строки переиспользуются — прокрутка не тормозит даже на больших таблицах
        grid.EnableRowVirtualization = true;
        grid.EnableColumnVirtualization = true;
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid, true);
    }

    private static void OnAutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.Column is DataGridTextColumn column)
        {
            // привязка по имени столбца через индексатор — безопасно для пробелов и кириллицы
            var binding = new Binding($"[{e.PropertyName}]") { Mode = BindingMode.OneWay };
            if (e.PropertyType == typeof(double)) binding.StringFormat = "N2";
            column.Binding = binding;
        }
        if (e.PropertyName == "ID") e.Column.Width = new DataGridLength(64);
    }

    /// <summary>Значение столбца ID у выбранной строки (или null, если ничего не выбрано).</summary>
    public static long? SelectedId(DataGrid grid) =>
        grid.SelectedItem is DataRowView row ? Convert.ToInt64(row["ID"]) : null;
}
