using System.Data;
using System.Windows;
using Microsoft.Win32;

namespace LibrarySystem.Services;

/// <summary>Единые окна сообщений.</summary>
public static class Ui
{
    public static void Error(string message) =>
        MessageBox.Show(message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>
    /// Показывает всплывающее уведомление; его регистрирует главное окно. Пока главного окна нет
    /// (например, на окне входа), сообщение выводится обычным окном.
    /// </summary>
    public static Action<string, ToastKind>? ToastHandler { get; set; }

    /// <summary>Операция выполнена (зелёное уведомление).</summary>
    public static void Success(string message) => Toast(message, ToastKind.Success);

    /// <summary>Сведения (латунное уведомление).</summary>
    public static void Info(string message) => Toast(message, ToastKind.Info);

    /// <summary>Подсказка пользователю: нужно что-то выбрать или заполнить.</summary>
    public static void Warn(string message) => Toast(message, ToastKind.Warning);

    /// <summary>Операция невозможна по правилам библиотеки (бордовое уведомление, висит дольше остальных).</summary>
    public static void Fail(string message) => Toast(message, ToastKind.Error);

    private static void Toast(string message, ToastKind kind)
    {
        if (ToastHandler is not null)
            ToastHandler(message, kind);
        else
            MessageBox.Show(message, "Библиотечная система", MessageBoxButton.OK,
                kind == ToastKind.Error ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    public static bool Confirm(string message) =>
        MessageBox.Show(message, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>Сохраняет таблицу в CSV: показывает диалог выбора файла и сообщает о результате.</summary>
    public static void SaveCsv(DataTable table, string name)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"{name}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Csv.Write(table, dialog.FileName);
            Audit.Log("Экспорт в CSV: " + name);
            Success("Файл сохранён:\n" + dialog.FileName);
        }
        catch (Exception ex)
        {
            Error("Не удалось сохранить файл: " + ex.Message);
        }
    }

    /// <summary>Сохраняет таблицу в документ Word (.docx): диалог выбора файла и сообщение о результате.</summary>
    public static void SaveWord(DataTable table, string name, string? title = null)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Документ Word (*.docx)|*.docx",
            FileName = $"{name}_{DateTime.Now:yyyyMMdd_HHmm}.docx",
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Word.Write(table, dialog.FileName, title);
            Audit.Log("Экспорт в Word: " + name);
            Success("Файл сохранён:\n" + dialog.FileName);
        }
        catch (Exception ex)
        {
            Error("Не удалось сохранить файл: " + ex.Message);
        }
    }
}
