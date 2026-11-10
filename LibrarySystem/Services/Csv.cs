using System.Data;
using System.IO;
using System.Text;

namespace LibrarySystem.Services;

/// <summary>Вывод таблицы в CSV: разделитель «;», UTF-8 с BOM — Excel открывает без «кракозябр».</summary>
public static class Csv
{
    private static string Escape(string s) =>
        s.Contains(';') || s.Contains('"') || s.Contains('\n') || s.Contains('\r')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;

    public static void Write(DataTable table, string path)
    {
        using var w = new StreamWriter(path, false, new UTF8Encoding(true));
        w.WriteLine(string.Join(";", table.Columns.Cast<DataColumn>().Select(c => Escape(c.ColumnName))));
        foreach (DataRow row in table.Rows)
            w.WriteLine(string.Join(";", row.ItemArray.Select(v => Escape(v is null or DBNull ? "" : Convert.ToString(v) ?? ""))));
    }
}
