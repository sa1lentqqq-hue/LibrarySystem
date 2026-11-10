namespace LibrarySystem.Data;

/// <summary>Настройки бизнес-правил и сведения об авторе.</summary>
public static class AppConfig
{
    public const double FinePerDay = 0.50;   // штраф за день просрочки, руб.
    public const int LoanDays = 14;          // срок выдачи, дней
    public const int MaxLoans = 5;           // максимум книг на руках у читателя
    public const int RenewDays = 7;          // на сколько дней продлевается выдача

    public const string Version = "2.3";
    public const string AuthorName = "Дейнеко Артур Сергеевич";
    public const string AuthorShort = "Дейнеко А. С.";
    public const string Group = "22о";
}
