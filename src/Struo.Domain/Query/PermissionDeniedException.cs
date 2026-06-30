// src/Struo.Domain/Query/PermissionDeniedException.cs
namespace Struo.Domain.Query;

/// <summary>
/// Thrown when the caller's resolved permissions deny an operation. The HTTP status (401 for an
/// anonymous caller, 403 for an authenticated one) is decided by the API exception handler using
/// <c>HttpContext.User</c> — not here — so this type stays free of any web dependency.
/// </summary>
public sealed class PermissionDeniedException(string message) : Exception(message);
