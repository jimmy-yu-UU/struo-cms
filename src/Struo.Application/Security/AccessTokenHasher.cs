using System.Security.Cryptography;
using System.Text;

namespace Struo.Application.Security;

/// <summary>Generates high-entropy permanent access tokens and hashes them for storage.
/// Only the hash is persisted; the plaintext token is shown once at generation.</summary>
public static class AccessTokenHasher
{
    public static (string token, string hash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32); // 256-bit
        var token = Base64UrlEncode(bytes);
        return (token, Hash(token));
    }

    public static string Hash(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(digest); // uppercase hex
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
