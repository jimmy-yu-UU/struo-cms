using System.Text;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Conservative content sniffing (audit L2). The upload path trusts the client-declared content type;
/// this checks the file's leading "magic bytes" against that declared type for the formats we know a
/// signature for (images + PDF). If the client claims one of those types but the bytes don't match, the
/// upload is rejected — this stops a script/HTML payload from being stored as <c>image/png</c> and later
/// served. Unknown declared types (e.g. text/plain, application/octet-stream) have no signature to check
/// and are allowed, so arbitrary uploads keep working (no behavior change to the default allow-all).
/// </summary>
public static class FileSignatureValidator
{
    /// <summary>
    /// True when <paramref name="header"/> is consistent with <paramref name="contentType"/>, or when
    /// the type has no known signature (allowed). False only when a known-signature type is claimed but
    /// the bytes don't match it.
    /// </summary>
    public static bool IsConsistent(ReadOnlySpan<byte> header, string? contentType)
    {
        switch ((contentType ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "image/png":
                return StartsWith(header, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            case "image/jpeg":
            case "image/jpg":
                return StartsWith(header, [0xFF, 0xD8, 0xFF]);
            case "image/gif":
                return StartsWithAscii(header, "GIF87a") || StartsWithAscii(header, "GIF89a");
            case "image/webp":
                return header.Length >= 12
                    && StartsWithAscii(header, "RIFF")
                    && StartsWithAscii(header[8..], "WEBP");
            case "application/pdf":
                return StartsWithAscii(header, "%PDF-");
            default:
                return true; // no signature known for this type -> nothing to contradict
        }
    }

    private static bool StartsWith(ReadOnlySpan<byte> header, ReadOnlySpan<byte> signature) =>
        header.Length >= signature.Length && header[..signature.Length].SequenceEqual(signature);

    private static bool StartsWithAscii(ReadOnlySpan<byte> header, string ascii) =>
        StartsWith(header, Encoding.ASCII.GetBytes(ascii));
}
