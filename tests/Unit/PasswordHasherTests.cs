using Brewvio.Helpers;

namespace Brewvio.Tests.Unit;

public class PasswordHasherTests
{
    // ── Hash / Verify roundtrip ───────────────────────────────────────────────

    [Fact]
    public void HashAndVerify_CorrectPassword_ReturnsTrue()
    {
        var hash = PasswordHasher.Hash("correct-horse-battery-staple");
        Assert.True(PasswordHasher.Verify("correct-horse-battery-staple", hash));
    }

    [Fact]
    public void HashAndVerify_WrongPassword_ReturnsFalse()
    {
        var hash = PasswordHasher.Hash("correct-horse-battery-staple");
        Assert.False(PasswordHasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void Hash_ProducesThreePartFormat()
    {
        var hash = PasswordHasher.Hash("password");
        Assert.Equal(3, hash.Split('.').Length);
    }

    [Fact]
    public void Hash_StartsWithCurrentIterationCount()
    {
        var hash = PasswordHasher.Hash("password");
        Assert.StartsWith("600000.", hash);
    }

    [Fact]
    public void Hash_TwoCallsSamePasword_ProduceDifferentHashes()
    {
        // Each call uses a random salt — two hashes for the same password must differ.
        var h1 = PasswordHasher.Hash("same");
        var h2 = PasswordHasher.Hash("same");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void Verify_EmptyStoredHash_ReturnsFalse()
    {
        Assert.False(PasswordHasher.Verify("anything", ""));
    }

    [Fact]
    public void Verify_NullStoredHash_ReturnsFalse()
    {
        Assert.False(PasswordHasher.Verify("anything", null!));
    }

    // ── Legacy 2-part format (100k iterations) ───────────────────────────────

    [Fact]
    public void Verify_LegacyFormat_AcceptsCorrectPassword()
    {
        // Produce a legacy-format hash manually: "salt.hash" at 100k iterations.
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "legacy-password", salt, 100_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        var stored = $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";

        Assert.True(PasswordHasher.Verify("legacy-password", stored));
    }

    [Fact]
    public void Verify_LegacyFormat_RejectsWrongPassword()
    {
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "legacy-password", salt, 100_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        var stored = $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";

        Assert.False(PasswordHasher.Verify("wrong-password", stored));
    }

    // ── Malformed stored values ───────────────────────────────────────────────

    [Theory]
    [InlineData("notbase64")]             // 1 part
    [InlineData("a.b.c.d")]              // 4 parts
    [InlineData("0.abc.abc")]            // zero iterations
    [InlineData("-1.abc.abc")]           // negative iterations
    [InlineData("abc.abc.abc")]          // non-numeric iterations
    [InlineData("600000.!!!.abc")]       // non-base64 salt
    [InlineData("600000.abc.!!!")]       // non-base64 hash
    public void Verify_MalformedStoredValue_ReturnsFalse(string stored)
    {
        Assert.False(PasswordHasher.Verify("password", stored));
    }

    // ── NeedsRehash ──────────────────────────────────────────────────────────

    [Fact]
    public void NeedsRehash_CurrentFormatHash_ReturnsFalse()
    {
        var hash = PasswordHasher.Hash("password");
        Assert.False(PasswordHasher.NeedsRehash(hash));
    }

    [Fact]
    public void NeedsRehash_LegacyFormatHash_ReturnsTrue()
    {
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "password", salt, 100_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        var stored = $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";

        Assert.True(PasswordHasher.NeedsRehash(stored));
    }

    [Fact]
    public void NeedsRehash_LowIterationModernFormat_ReturnsTrue()
    {
        // A 3-part hash that uses fewer than 600k iterations should be flagged.
        var salt = new byte[16];
        var hash = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "password", salt, 200_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        var stored = $"200000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";

        Assert.True(PasswordHasher.NeedsRehash(stored));
    }

    [Fact]
    public void NeedsRehash_EmptyStoredValue_ReturnsTrue()
    {
        // Can't parse → treat as needing rehash.
        Assert.True(PasswordHasher.NeedsRehash(""));
    }

    [Fact]
    public void NeedsRehash_MalformedStoredValue_ReturnsTrue()
    {
        Assert.True(PasswordHasher.NeedsRehash("not-a-valid-hash"));
    }
}
