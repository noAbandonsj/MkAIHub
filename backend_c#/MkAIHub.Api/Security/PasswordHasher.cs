using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Konscious.Security.Cryptography;

namespace MkAIHub.Api.Security;

/// <summary>
/// Argon2id password hashing with parameters matching argon2-cffi
/// (time_cost=2, memory_cost=19456, parallelism=2, hash_len=32, salt_len=16),
/// so hashes are interchangeable between the Python and C# backends.
/// </summary>
public static class PasswordHasher
{
    private const int TimeCost = 2;
    private const int MemoryKiB = 19_456;
    private const int Parallelism = 2;
    private const int HashLength = 32;
    private const int SaltLength = 16;

    private static readonly Regex PhcRegex = new(
        @"^\$(?<type>argon2(?:id|i|d))\$v=(?<version>\d+)\$m=(?<m>\d+),t=(?<t>\d+),p=(?<p>\d+)\$(?<salt>[A-Za-z0-9+/]+)\$(?<hash>[A-Za-z0-9+/]+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void ValidatePassword(string password)
    {
        if (password.Length is < 8 or > 128)
        {
            throw new ArgumentException("Password must be between 8 and 128 characters");
        }
    }

    public static string HashPassword(string password)
    {
        ValidatePassword(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var hash = ComputeHash(Encoding.UTF8.GetBytes(password), salt, MemoryKiB, TimeCost, Parallelism, HashLength);
        return $"$argon2id$v=19$m={MemoryKiB},t={TimeCost},p={Parallelism}${EncodeBase64(salt)}${EncodeBase64(hash)}";
    }

    /// <summary>Verify a password, returning false for all malformed/mismatched hashes.</summary>
    public static bool VerifyPassword(string password, string passwordHash)
    {
        var match = PhcRegex.Match(passwordHash ?? string.Empty);
        if (!match.Success)
        {
            return false;
        }
        if (match.Groups["type"].Value != "argon2id" || match.Groups["version"].Value != "19")
        {
            return false;
        }
        var memory = int.Parse(match.Groups["m"].Value);
        var iterations = int.Parse(match.Groups["t"].Value);
        var parallelism = int.Parse(match.Groups["p"].Value);
        var salt = DecodeBase64(match.Groups["salt"].Value);
        var expected = DecodeBase64(match.Groups["hash"].Value);
        if (salt.Length == 0 || expected.Length == 0)
        {
            return false;
        }
        byte[] actual;
        try
        {
            actual = ComputeHash(
                Encoding.UTF8.GetBytes(password), salt, memory, iterations, parallelism, expected.Length);
        }
        catch (ArgumentException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Hash a raw session cookie token before it reaches the database.</summary>
    public static string HashSessionToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static byte[] ComputeHash(
        byte[] password,
        byte[] salt,
        int memoryKiB,
        int timeCost,
        int parallelism,
        int hashLength)
    {
        using var argon2 = new Argon2id(password)
        {
            Salt = salt,
            MemorySize = memoryKiB,
            Iterations = timeCost,
            DegreeOfParallelism = parallelism,
        };
        return argon2.GetBytes(hashLength);
    }

    private static string EncodeBase64(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=');

    private static byte[] DecodeBase64(string value)
    {
        var padding = (4 - value.Length % 4) % 4;
        try
        {
            return Convert.FromBase64String(value + new string('=', padding));
        }
        catch (FormatException)
        {
            return Array.Empty<byte>();
        }
    }
}
