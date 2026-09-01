namespace Struo.Application.Security;

/// <summary>
/// Thrown when revoking a user's existing sessions fails after the operation that must trigger it (a
/// password change, a user deletion) has ALREADY succeeded and committed. There is nothing to roll
/// back — the underlying write already happened — this exists purely so the HTTP response tells the
/// caller "some sessions may still be live" instead of an indistinguishable success. The constructor
/// message must stay client-safe (it is surfaced verbatim, per <c>DomainErrorMap</c>'s convention);
/// pass the real failure as <paramref name="inner"/> for server-side logging instead.
/// </summary>
public sealed class SessionRevocationFailedException(string message, Exception inner) : Exception(message, inner);
