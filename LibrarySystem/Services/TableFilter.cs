using System.Data;
using System.Text;

namespace LibrarySystem.Services;

/// <summary>Строка RowFilter для DataView: поиск текста по всем столбцам (регистр не важен, в том числе для кириллицы).</summary>
public static class TableFilter
{
    public static string Like(DataTable table, string text)
    {
        text = text.Trim();
        if (text.Length == 0) return "";

        var sb = new StringBuilder();
        foreach (char c in text)
        {
            if (c == '\'') sb.Append("''");
            else if (c is '[' or ']' or '*' or '%') sb.Append('[').Append(c).Append(']');
            else sb.Append(c);
        }
        string pattern = sb.ToString();
        return string.Join(" OR ", table.Columns.Cast<DataColumn>()
            .Select(c => $"Convert([{c.ColumnName}], 'System.String') LIKE '%{pattern}%'"));
    }
}
