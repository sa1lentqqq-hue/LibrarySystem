namespace LibrarySystem.Data;

/// <summary>Текущий вошедший пользователь и его роль.</summary>
public static class Session
{
    public static long UserId { get; set; }
    public static string Login { get; set; } = "";
    public static string FullName { get; set; } = "";
    public static string Role { get; set; } = "";

    public static bool IsAdmin => Role == "admin";
    public static string RoleName => IsAdmin ? "администратор" : "библиотекарь";

    public static void Clear()
    {
        UserId = 0;
        Login = FullName = Role = "";
    }
}
