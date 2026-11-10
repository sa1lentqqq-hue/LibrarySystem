using System.Data;
using System.Globalization;
using LibrarySystem.Data;

namespace LibrarySystem.Services;

/// <summary>Данные для главной страницы: просрочки, выдачи по жанрам и по неделям. Запросы используют представления БД.</summary>
public static class DashboardService
{
    /// <summary>Сколько выдач просрочено и у скольких читателей (по представлению v_overdue_loans).</summary>
    public static (long Loans, long Readers) GetOverdue()
    {
        DataTable t = Database.Query("SELECT COUNT(*) AS c, COUNT(DISTINCT reader_id) AS r FROM v_overdue_loans");
        return (Convert.ToInt64(t.Rows[0]["c"]), Convert.ToInt64(t.Rows[0]["r"]));
    }

    /// <summary>Число выдач по жанрам, больше — выше (по представлению v_genre_loans).</summary>
    public static IReadOnlyList<(string Genre, long Count)> GetGenreLoans() =>
        Database.Query("SELECT genre, loans_count FROM v_genre_loans ORDER BY loans_count DESC, genre")
            .Rows.Cast<DataRow>()
            .Select(r => (Convert.ToString(r["genre"]) ?? "", Convert.ToInt64(r["loans_count"])))
            .ToList();

    /// <summary>
    /// Число выдач по неделям за последние <paramref name="weeks"/> недель (от старых к новым).
    /// Последняя неделя заканчивается сегодняшним днём; Start — первый день недели.
    /// </summary>
    public static IReadOnlyList<(DateTime Start, long Count)> GetWeeklyLoans(int weeks = 8)
    {
        var result = new List<(DateTime, long)>();
        DateTime today = DateTime.Today;
        for (int i = weeks - 1; i >= 0; i--)
        {
            DateTime end = today.AddDays(-7 * i);
            DateTime start = end.AddDays(-6);
            long count = Database.Scalar<long>(
                "SELECT COUNT(*) FROM loans WHERE issued_at BETWEEN @from AND @to",
                ("@from", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("@to", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
            result.Add((start, count));
        }
        return result;
    }
}
