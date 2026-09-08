using System.Security.Cryptography;
using System.Text;

namespace CashCafe.App.Core.Services;

/// <summary>
/// The admin PIN, stored only as a hash.
///
/// PBKDF2-HMAC-SHA256 at 310 000 iterations with a random 16-byte salt. The PIN itself is
/// never written anywhere, so it cannot be read back out of the database, a backup, or an
/// export — losing it means the documented reset, which requires local administrator rights
/// on the café computer and is written to the audit log where it cannot be hidden.
/// </summary>
public static class AdminPin
{
    private const int Iterations = 310_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public const int MinimumLength = 4;
    public const int MaximumLength = 12;

    public static string Hash(string pin)
    {
        Validate(pin);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(pin, salt);

        return $"pbkdf2-sha256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "pbkdf2-sha256") return false;
        if (!int.TryParse(parts[1], out var iterations)) return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin), salt, iterations, HashAlgorithmName.SHA256, expected.Length);

        // Fixed-time comparison: a PIN is short enough that a timing difference would matter.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public static void Validate(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || pin.Length < MinimumLength || pin.Length > MaximumLength)
            throw new ArgumentException($"The PIN must be {MinimumLength}–{MaximumLength} characters.", nameof(pin));

        if (!pin.All(char.IsDigit))
            throw new ArgumentException("The PIN must be digits only.", nameof(pin));
    }

    private static byte[] Derive(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(pin), salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
}
