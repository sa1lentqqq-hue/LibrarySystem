using LibrarySystem.Controls;

namespace LibrarySystem.Pages;

public enum FieldKind { Text, Integer, Real, Date, Lookup, Choice, Password }

/// <summary>Описание одного поля формы добавления/редактирования записи.</summary>
public sealed class FieldDef
{
    public string Column { get; }
    public string Caption { get; }
    public FieldKind Kind { get; }
    public bool Required { get; }
    public string? LookupSql { get; init; }       // для Lookup: SELECT id, name FROM ...
    public string[]? Choices { get; init; }       // для Choice

    public FieldDef(string column, string caption, FieldKind kind, bool required = false)
    {
        Column = column;
        Caption = caption;
        Kind = kind;
        Required = required;
    }
}

/// <summary>Настройка универсальной страницы-таблицы: запрос списка, поля формы и права действий.</summary>
public sealed record TableConfig(
    string Table, string ListSql, IReadOnlyList<FieldDef> Fields,
    bool CanAdd, bool CanEdit, bool CanDelete,
    string? DefaultSort = null, bool DefaultDescending = false, IReadOnlyList<DropFilter>? Filters = null);
