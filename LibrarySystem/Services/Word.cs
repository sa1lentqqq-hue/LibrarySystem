using System.Data;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LibrarySystem.Services;

/// <summary>
/// Вывод таблицы в документ Word (.docx). Файл собирается средствами .NET без сторонних библиотек:
/// внутри .docx — ZIP-архив с разметкой WordprocessingML (типы содержимого, связи, документ).
/// Оформление — в стиле программы: заголовок Georgia цветом тёмного дерева, таблица на «странице книги».
/// </summary>
public static class Word
{
    private static readonly CultureInfo Ru = new("ru-RU");

    /// <summary>Экранирование текста для XML.</summary>
    private static string Esc(string s) => s
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\"", "&quot;").Replace("'", "&apos;");

    /// <summary>Записывает текстовый файл (часть .docx) в архив.</summary>
    private static void Entry(ZipArchive zip, string path, string content)
    {
        ZipArchiveEntry entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    /// <summary>Абзац с прямым форматированием (без таблицы стилей — Word открывает такой файл без вопросов).</summary>
    private static string Paragraph(string text, string font, int halfPoints, bool bold, string color, string spacing = "")
    {
        string sp = spacing.Length == 0 ? "" : $"<w:spacing {spacing}/>";
        return "<w:p><w:pPr>" + sp + "</w:pPr><w:r><w:rPr>" +
               $"<w:rFonts w:ascii=\"{font}\" w:hAnsi=\"{font}\" w:cs=\"{font}\"/>" +
               (bold ? "<w:b/>" : "") +
               $"<w:color w:val=\"{color}\"/><w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/>" +
               "</w:rPr><w:t xml:space=\"preserve\">" + text + "</w:t></w:r></w:p>";
    }

    /// <summary>Ячейка таблицы: шапка — тёмное дерево с белым текстом, тело — «страница книги».</summary>
    private static string Cell(string text, bool header)
    {
        string shd = header
            ? "<w:shd w:val=\"clear\" w:fill=\"3B2A20\"/>"
            : "<w:shd w:val=\"clear\" w:fill=\"FBF6EA\"/>";
        return "<w:tc><w:tcPr>" + shd + "<w:tcVAlign w:val=\"center\"/></w:tcPr>" +
               "<w:p><w:r><w:rPr>" +
               "<w:rFonts w:ascii=\"Segoe UI\" w:hAnsi=\"Segoe UI\" w:cs=\"Segoe UI\"/>" +
               (header ? "<w:b/><w:color w:val=\"FFFFFF\"/>" : "<w:color w:val=\"2E2620\"/>") +
               "<w:sz w:val=\"19\"/><w:szCs w:val=\"19\"/>" +
               "</w:rPr><w:t xml:space=\"preserve\">" + text + "</w:t></w:r></w:p></w:tc>";
    }

    /// <summary>Сохраняет таблицу в файл .docx: заголовок, дата формирования и таблица данных.</summary>
    public static void Write(DataTable table, string path, string? title = null)
    {
        string head = string.IsNullOrWhiteSpace(title) ? "Отчёт" : title.Trim();
        var sb = new StringBuilder(64 * 1024);

        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>");

        sb.Append(Paragraph(Esc(head) + " — Библиотечная система", "Georgia", 36, true, "3B2A20", "w:after=\"120\""));
        sb.Append(Paragraph("Дата формирования: " + DateTime.Now.ToString("d MMMM yyyy г., HH:mm", Ru),
            "Segoe UI", 19, false, "75685B", "w:after=\"240\""));

        // Таблица: рамки цветом «латуни», повторяющаяся шапка на каждой странице
        sb.Append("<w:tbl><w:tblPr><w:tblW w:w=\"10000\" w:type=\"dxa\"/>" +
                  "<w:tblBorders>" +
                  "<w:top w:val=\"single\" w:sz=\"4\" w:color=\"B9A98C\"/>" +
                  "<w:left w:val=\"single\" w:sz=\"4\" w:color=\"B9A98C\"/>" +
                  "<w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"B9A98C\"/>" +
                  "<w:right w:val=\"single\" w:sz=\"4\" w:color=\"B9A98C\"/>" +
                  "<w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"B9A98C\"/>" +
                  "<w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"B9A98C\"/>" +
                  "</w:tblBorders>" +
                  "<w:tblCellMar>" +
                  "<w:top w:w=\"60\" w:type=\"dxa\"/><w:left w:w=\"100\" w:type=\"dxa\"/>" +
                  "<w:bottom w:w=\"60\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/>" +
                  "</w:tblCellMar></w:tblPr>");

        sb.Append("<w:tr><w:trPr><w:tblHeader/></w:trPr>");
        foreach (DataColumn column in table.Columns)
            sb.Append(Cell(Esc(column.ColumnName), header: true));
        sb.Append("</w:tr>");

        foreach (DataRow row in table.Rows)
        {
            sb.Append("<w:tr>");
            foreach (object? value in row.ItemArray)
                sb.Append(Cell(Esc(value is null or DBNull ? "" : Convert.ToString(value, Ru) ?? ""), header: false));
            sb.Append("</w:tr>");
        }

        sb.Append("</w:tbl>");

        // Пустой абзац после таблицы — Word требует, чтобы таблица не была последним элементом тела
        sb.Append("<w:p/>");

        sb.Append("<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/>" +
                  "<w:pgMar w:top=\"850\" w:right=\"850\" w:bottom=\"850\" w:left=\"850\" " +
                  "w:header=\"0\" w:footer=\"0\" w:gutter=\"0\"/></w:sectPr>");
        sb.Append("</w:body></w:document>");

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        Entry(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
            "</Types>");
        Entry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
            "</Relationships>");
        Entry(zip, "word/document.xml", sb.ToString());
    }
}
