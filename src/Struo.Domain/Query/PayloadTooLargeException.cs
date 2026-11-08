namespace Struo.Domain.Query;

/// <summary>
/// A request body or upload exceeded its configured cap: the actual byte stream passed the upload cap
/// even though the client's declared Content-Length did not, or the declared length was already over it.
/// Surfaced as HTTP 413 by the API-layer error map, distinct from <see cref="QueryException"/>'s 400 so
/// callers can tell "your request body is malformed" apart from "your upload is too big".
/// </summary>
public sealed class PayloadTooLargeException : Exception
{
    public PayloadTooLargeException(string message) : base(message) { }

    /// <summary>A refusal that names the limit: <paramref name="field"/> is the request part that was
    /// too large and <paramref name="maxBytes"/> the cap, which the error carries as a detail.</summary>
    public PayloadTooLargeException(string message, string field, long maxBytes) : base(message)
    {
        Field = field;
        MaxBytes = maxBytes;
    }

    /// <summary>The request part that exceeded the cap, or null when the refusal names no limit.</summary>
    public string? Field { get; }

    /// <summary>The cap in bytes, or null when the refusal names no limit.</summary>
    public long? MaxBytes { get; }
}
