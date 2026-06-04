using System.Globalization;
using System.Security.Cryptography;

namespace Brewvio.Helpers;

// PBKDF2 (SHA256) password hashing.
//
// New hashes are stored as "iterations.salt.hash" (iterations in plain text; salt + hash base64),
// using OWASP's current PBKDF2-SHA256 guidance of 600,000 iterations. Older hashes written by a
// previous version used a fixed 100,000 iterations and the 2-part "salt.hash" format — Verify still
// accepts those, so existing accounts keep working and are transparently upgraded on next login.
public static class PasswordHasher
{
    private const int Iterations = 600_000;   // OWASP PBKDF2-SHA256 recommendation
    private const int LegacyIterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (!TryParse(stored, out var iterations, out var salt, out var expected)) return false;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // True when the stored hash uses an older parameter set and should be re-hashed (e.g. after a
    // successful login) to bring the account up to the current iteration count.
    public static bool NeedsRehash(string stored) =>
        !TryParse(stored, out var iterations, out _, out _) || iterations < Iterations;

    private static bool TryParse(string stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = LegacyIterations;
        salt = Array.Empty<byte>();
        hash = Array.Empty<byte>();
        if (string.IsNullOrEmpty(stored)) return false;

        var parts = stored.Split('.');
        try
        {
            switch (parts.Length)
            {
                case 2: // legacy "salt.hash" => fixed 100k iterations
                    iterations = LegacyIterations;
                    salt = Convert.FromBase64String(parts[0]);
                    hash = Convert.FromBase64String(parts[1]);
                    return true;
                case 3: // current "iterations.salt.hash"
                    if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out iterations) || iterations <= 0)
                        return false;
                    salt = Convert.FromBase64String(parts[1]);
                    hash = Convert.FromBase64String(parts[2]);
                    return true;
                default:
                    return false;
            }
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
