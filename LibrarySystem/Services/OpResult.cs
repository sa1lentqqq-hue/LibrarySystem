namespace LibrarySystem.Services;

/// <summary>Результат операции: успех/ошибка и сообщение для пользователя.</summary>
public readonly record struct OpResult(bool Ok, string Message)
{
    public static OpResult Success(string message) => new(true, message);
    public static OpResult Fail(string message) => new(false, message);
}
