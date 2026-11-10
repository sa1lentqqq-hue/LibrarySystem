using System.Data;
using LibrarySystem.Data;

namespace LibrarySystem.Services;

/// <summary>Одна подсказка в выпадающем списке поиска. Head/Match/Tail нужны, чтобы выделить введённые буквы жирным.</summary>
public sealed record Suggestion(string Text, string Kind, int Start, int Length)
{
    public string Head => Text[..Start];
    public string Match => Text.Substring(Start, Length);
    public string Tail => Text[(Start + Length)..];
}

/// <summary>
/// Подсказки при вводе: названия книг, авторы и читатели, начинающиеся на введённые буквы (затем — слова внутри названия,
/// например фамилия автора). Список берётся из памяти и обновляется при каждом заходе в строку поиска.
/// </summary>
public static class SuggestService
{
    private sealed record Entry(string Text, string Kind);

    private static List<Entry>? _cache;

    /// <summary>Сбросить кэш: при следующем вводе данные будут прочитаны из БД заново.</summary>
    public static void Invalidate() => _cache = null;

    private static List<Entry> Load()
    {
        var list = new List<Entry>();
        var titles = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var authors = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        foreach (DataRow row in Database.Query("SELECT title, author FROM books").Rows)
        {
            string title = Convert.ToString(row["title"]) ?? "";
            string author = Convert.ToString(row["author"]) ?? "";
            if (title.Length > 0 && titles.Add(title)) list.Add(new Entry(title, "Книга"));
            if (author.Length > 0 && authors.Add(author)) list.Add(new Entry(author, "Автор"));
        }
        foreach (DataRow row in Database.Query("SELECT full_name FROM readers").Rows)
        {
            string name = Convert.ToString(row["full_name"]) ?? "";
            if (name.Length > 0) list.Add(new Entry(name, "Читатель"));
        }
        return list;
    }

    public static IReadOnlyList<Suggestion> Find(string query, int perKind = 4, int max = 8)
    {
        query = query.Trim();
        if (query.Length == 0) return Array.Empty<Suggestion>();
        _cache ??= Load();

        var hits = new List<(Suggestion Item, int Tier)>();
        foreach (Entry entry in _cache)
        {
            int start = FindStart(entry.Text, query, out int tier);
            if (start >= 0) hits.Add((new Suggestion(entry.Text, entry.Kind, start, query.Length), tier));
        }

        // сначала те, что НАЧИНАЮТСЯ на введённое, затем совпадения по слову внутри; внутри группы — по алфавиту
        var perKindCount = new Dictionary<string, int>();
        var result = new List<Suggestion>();
        foreach (var hit in hits.OrderBy(h => h.Tier).ThenBy(h => h.Item.Text, StringComparer.CurrentCultureIgnoreCase))
        {
            perKindCount.TryGetValue(hit.Item.Kind, out int count);
            if (count >= perKind) continue;
            perKindCount[hit.Item.Kind] = count + 1;
            result.Add(hit.Item);
            if (result.Count >= max) break;
        }
        return result;
    }

    private static int FindStart(string text, string query, out int tier)
    {
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) { tier = 0; return 0; }
        tier = 1;
        for (int i = 1; i + query.Length <= text.Length; i++)
        {
            if (char.IsLetterOrDigit(text[i - 1]) || !char.IsLetterOrDigit(text[i])) continue;    // только начало слова
            if (string.Compare(text, i, query, 0, query.Length, StringComparison.OrdinalIgnoreCase) == 0) return i;
        }
        return -1;
    }
}
