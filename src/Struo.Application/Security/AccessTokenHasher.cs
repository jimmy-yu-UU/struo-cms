using System.Security.Cryptography;
using System.Text;

namespace Struo.Application.Security;

/// <summary>Generates high-entropy permanent access tokens and hashes them for storage.
/// Only the hash is persisted; the plaintext token is shown once at generation.
///
/// Measured: on SQL Server, a unique index on a nullable column rejects the second <c>NULL</c> row.
/// Ruled out: a unique index on <c>users.accesstoken</c>, since most users hold no token and would fail to
/// write. Unknown: how the other backends treat several <c>NULL</c> values under a unique index.
/// If undone: creating a second user without a token fails on SQL Server.
///
/// Full write-up: docs/ai/decisions/accesstoken-column-has-no-unique-index.md
/// </summary>
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
