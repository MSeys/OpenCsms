namespace OpenCsms.Domain;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// The machine API key's rules: a prefixed, URL-safe random value that is shown once at tenant
/// registration, and the SHA-256 hash a store keeps so a leaked row cannot be replayed as a
/// credential.
/// </summary>
public static class MachineApiKey
{
    /// <summary>The prefix every key carries, so a key is recognizable wherever it appears.</summary>
    public const string Prefix = "ocsms_";

    private const int KeyBytes = 32;

    /// <summary>Generates a fresh key; the caller returns it once and stores only <see cref="Hash"/>.</summary>
    public static string Create()
        => Prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>The key's store form: lower-case hex, so the unique index compares one spelling.</summary>
    public static string Hash(string apiKey)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey))).ToLowerInvariant();
}
