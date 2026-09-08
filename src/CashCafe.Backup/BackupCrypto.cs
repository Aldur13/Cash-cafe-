using System.Security.Cryptography;
using System.Text;

namespace CashCafe.Backup;

/// <summary>
/// Encrypts anything that leaves the café computer.
///
/// AES-256-GCM, with the key derived from the school's passphrase by PBKDF2-SHA256 at
/// 310 000 iterations and a fresh 16-byte salt for every backup. GCM rather than CBC
/// because it is authenticated: a backup that has been altered — by a failed upload, a bad
/// disk, or somebody — fails to decrypt rather than quietly restoring wrong numbers.
///
/// There is no recovery path. Nobody, including whoever wrote this, can decrypt a backup
/// without the passphrase. That is the point, and it is why the wiki tells the school to
/// write it down somewhere two people can find it.
/// </summary>
public static class BackupCrypto
{
    private const int SaltBytes = 16;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private const int KeyBytes = 32;
    private const int Iterations = 310_000;

    /// <summary>Identifies an encrypted backup, and pins the format so a future version can change it.</summary>
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("CCBK1\0");

    public const int MinimumPassphraseLength = 12;

    /// <summary>File layout: magic ‖ salt ‖ nonce ‖ ciphertext ‖ tag.</summary>
    public static void Encrypt(Stream plain, Stream destination, string passphrase)
    {
        ValidatePassphrase(passphrase);

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var key = DeriveKey(passphrase, salt);

        using var buffer = new MemoryStream();
        plain.CopyTo(buffer);
        var content = buffer.ToArray();

        var cipher = new byte[content.Length];
        var tag = new byte[TagBytes];

        using (var aes = new AesGcm(key, TagBytes))
            aes.Encrypt(nonce, content, cipher, tag, Magic);

        CryptographicOperations.ZeroMemory(key);

        destination.Write(Magic);
        destination.Write(salt);
        destination.Write(nonce);
        destination.Write(cipher);
        destination.Write(tag);
        destination.Flush();
    }

    public static void Decrypt(Stream encrypted, Stream destination, string passphrase)
    {
        using var buffer = new MemoryStream();
        encrypted.CopyTo(buffer);
        var all = buffer.ToArray();

        var header = Magic.Length + SaltBytes + NonceBytes;
        if (all.Length < header + TagBytes || !all.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("This is not a Cash Café encrypted backup.");

        var salt = all.AsSpan(Magic.Length, SaltBytes).ToArray();
        var nonce = all.AsSpan(Magic.Length + SaltBytes, NonceBytes).ToArray();
        var cipher = all.AsSpan(header, all.Length - header - TagBytes).ToArray();
        var tag = all.AsSpan(all.Length - TagBytes, TagBytes).ToArray();

        var key = DeriveKey(passphrase, salt);
        var plain = new byte[cipher.Length];

        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(nonce, cipher, tag, plain, Magic);
        }
        catch (CryptographicException)
        {
            // The same message for a wrong passphrase and for a damaged file, because from
            // here they are genuinely indistinguishable and guessing would mislead.
            throw new InvalidDataException(
                "Could not open this backup. Either the passphrase is wrong or the file is damaged.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        destination.Write(plain);
        destination.Flush();
    }

    public static bool LooksEncrypted(Stream file)
    {
        if (!file.CanSeek) return false;

        var start = file.Position;
        var header = new byte[Magic.Length];
        var read = file.Read(header, 0, header.Length);
        file.Position = start;

        return read == header.Length && header.AsSpan().SequenceEqual(Magic);
    }

    public static void ValidatePassphrase(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < MinimumPassphraseLength)
            throw new ArgumentException(
                $"The backup passphrase must be at least {MinimumPassphraseLength} characters.", nameof(passphrase));
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase), salt, Iterations, HashAlgorithmName.SHA256, KeyBytes);
}
