// src/Struo.Domain/Query/InvalidVersionException.cs
namespace Struo.Domain.Query;

/// <summary>
/// Thrown when an update body carries a <c>version</c> that is present, not JSON null, and not a
/// parseable 64-bit integer. The API maps this to HTTP 400 INVALID_VERSION. Web-free by design.
/// </summary>
public sealed class InvalidVersionException(string message) : Exception(message);
