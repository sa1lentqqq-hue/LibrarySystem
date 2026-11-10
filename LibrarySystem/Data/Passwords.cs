using System.Security.Cryptography;
using System.Text;

namespace LibrarySystem.Data;

/// <summary>
/// Пароли хранятся только в виде хэша: PBKDF2-HMAC-SHA256, 100 000 итераций,
/// у каждого пользователя своя случайная соль. Исходный пароль нигде не сохраняется.
/// </summary>
public static class Passwords
{
    private const int Iterations = 100_000;
    private const int HashBytes = 32;
    private const int SaltBytes = 16;

    public static string NewSalt() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(SaltBytes)).ToLowerInvariant();

    public static string Hash(string password, string saltHex) =>
        Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password), Convert.FromHexString(saltHex),
            Iterations, HashAlgorithmName.SHA256, HashBytes)).ToLowerInvariant();

    public static bool Verify(string password, string saltHex, string expectedHex) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Hash(password, saltHex)),
            Encoding.ASCII.GetBytes(expectedHex.ToLowerInvariant()));
}
