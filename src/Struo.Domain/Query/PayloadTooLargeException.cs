namespace Struo.Domain.Query;

/// <summary>
/// The actual byte stream exceeded the configured upload cap even though the client's
/// declared Content-Length passed the up-front check (a lying/streaming client). Surfaced as
/// HTTP 413 by the API-layer error map, distinct from <see cref="QueryException"/>'s 400 so
/// callers can tell "your request body is malformed" apart from "your upload is too big".
/// </summary>
public sealed class PayloadTooLargeException(string message) : Exception(message);
