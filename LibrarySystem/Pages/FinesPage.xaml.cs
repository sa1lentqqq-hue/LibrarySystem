using System.Data;
using System.Windows;
using System.Windows.Controls;
using LibrarySystem.Controls;
using LibrarySystem.Data;
using LibrarySystem.Services;

namespace LibrarySystem.Pages;

/// <summary>Штрафы: просмотр, оплата, пересчёт за просрочку, удаление (только администратор).</summary>
public partial class FinesPage : UserControl, IRefreshable
{
    private DataTable _data = new();

    public FinesPage(string? initialStatus = null)
    {
        InitializeComponent();
        GridHelper.Setup(FinesGrid);
        DeleteButton.Visibility = Session.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        RateText.Text = $"Ставка: {AppConfig.FinePerDay:N2} руб. за каждый день просрочки";
        // по умолчанию — свежие штрафы сверху (по дате); фильтр по статусу оплаты
        Filter.Configure(FinesGrid, "Дата", true, new DropFilter("Статус", new[] { "Не оплачен", "Оплачен" }));
        if (!string.IsNullOrEmpty(initialStatus)) Filter.SelectDrop("Статус", initialStatus);   // например, «Не оплачен» с главной
        Reload();
    }

    private void Reload()
    {
        _data = Database.Query(
            @"SELECT f.id AS [ID], r.full_name AS [Читатель], b.title AS [Книга], f.reason AS [Причина],
                     f.amount AS [Сумма], CASE f.paid WHEN 1 THEN 'Оплачен' ELSE 'Не оплачен' END AS [Статус],
                     f.created_at AS [Дата]
              FROM fines f JOIN loans l ON l.id = f.loan_id JOIN readers r ON r.id = l.reader_id JOIN books b ON b.id = l.book_id
              ORDER BY f.paid, f.id DESC");
        Filter.Bind(_data);
        FinesGrid.ItemsSource = _data.DefaultView;
        double total = Database.Scalar<double>("SELECT COALESCE(ROUND(SUM(amount), 2), 0) FROM fines WHERE paid = 0");
        TotalText.Text = $"К оплате всего: {total:N2} руб.";
    }

    public void Refresh() => Reload();

    private long? SelectedFine()
    {
        long? id = GridHelper.SelectedId(FinesGrid);
        if (id is null) Ui.Warn("Выберите штраф в таблице.");
        return id;
    }

    private void OnPay(object sender, RoutedEventArgs e)
    {
        if (SelectedFine() is not long id) return;
        if (!Ui.Confirm("Отметить штраф как оплаченный?")) return;
        OpResult result = LoanService.PayFine(id);
        if (result.Ok) Ui.Success(result.Message); else Ui.Fail(result.Message);
        Reload();
    }

    private void OnPayAll(object sender, RoutedEventArgs e)
    {
        if (SelectedFine() is not long id) return;
        if (!Ui.Confirm("Оплатить сразу все неоплаченные штрафы этого читателя?")) return;
        OpResult result = LoanService.PayReaderFines(id);
        if (result.Ok) Ui.Success(result.Message); else Ui.Fail(result.Message);
        Reload();
    }

    private void OnRecalc(object sender, RoutedEventArgs e)
    {
        LoanService.RecalculateOverdueFines();
        Reload();
        Ui.Success($"Штрафы пересчитаны. Ставка: {AppConfig.FinePerDay:N2} руб. за день просрочки.");
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (!Session.IsAdmin) { Ui.Error("Удалять штрафы может только администратор."); return; }
        if (SelectedFine() is not long id) return;
        if (!Ui.Confirm("Удалить штраф?\nЭто действие нельзя отменить.")) return;
        Database.Execute("DELETE FROM fines WHERE id = @id", ("@id", id));
        Audit.Log($"Удалён штраф {id}");
        Reload();
    }
}
