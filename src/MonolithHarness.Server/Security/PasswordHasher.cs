using System.Security.Cryptography;
using System.Text;

namespace MonolithHarness.Server.Security;

/// <summary>PBKDF2-HMAC-SHA512 with a per-password salt. The format is versioned so the cost can be raised later.</summary>
public static class PasswordHasher
{
    const string Scheme = "pbkdf2-sha512";
    const int Iterations = 210_000;
    const int SaltSize = 16;
    const int HashSize = 32;
    // Verified for unknown accounts so that the response time does not reveal whether an e-mail exists.
    static readonly string Decoy = Hash("decoy-password-never-matches");

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA512, HashSize);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored, out bool needsRehash)
    {
        needsRehash = false;
        var parts = (stored ?? Decoy).Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations) || iterations < 1)
        {
            Rfc2898DeriveBytes.Pbkdf2("x"u8.ToArray(), new byte[SaltSize], Iterations, HashAlgorithmName.SHA512, HashSize);
            return false;
        }
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromBase64String(parts[2]), iterations, HashAlgorithmName.SHA512, expected.Length);
        var match = CryptographicOperations.FixedTimeEquals(actual, expected) && stored != null;
        needsRehash = match && iterations < Iterations;
        return match;
    }

    /// <summary>High-entropy opaque secret (session or recovery token): URL-safe base64 of 32 random bytes.</summary>
    public static string NewToken(string prefix) => prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Tokens are random, so a fast hash is enough for storage and lookups.</summary>
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
