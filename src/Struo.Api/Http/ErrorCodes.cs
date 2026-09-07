namespace Struo.Api.Http;

public static class ErrorCodes
{
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    // Optimistic-lock (version CAS miss) split out from generic Conflict so the frontend's
    // "changed by someone else" recovery keys on this alone. Exception-driven only — the status→code
    // reverse map (ForStatus) deliberately keeps 409 → Conflict for inline ApiResults.Fail callers.
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string BadUserInput = "BAD_USER_INPUT";
    public const string Validation = "VALIDATION";
    public const string Internal = "INTERNAL_SERVER_ERROR";
    // Three independent producers write this code, none of them through DomainErrorMap (no
    // exception is thrown for any of them): the per-client-IP login and change-password rate
    // limiters (Program.cs, Microsoft.AspNetCore.RateLimiting) write it directly from their shared
    // OnRejected callback, and AuthController.Login's per-account ILoginAttemptThrottle writes it
    // directly when a throttled login is rejected before authentication even runs.
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    // A lying/streaming upload whose actual bytes exceed FileStorageOptions.MaxUploadBytes
    // even though the declared Content-Length passed the up-front check.
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

    // "Your current password is wrong" on PUT /api/users/{id}/password. Deliberately NOT 401/
    // Unauthorized: the caller IS authenticated (they hold a session), and the SPA's global 401
    // handler clears the session for every 401 — so reusing Unauthorized here logged the user out
    // on a typo. 400 + this code keeps the global handler's meaning single ("your session is gone").
    public const string InvalidCurrentPassword = "INVALID_CURRENT_PASSWORD";

    // Self-service password change attempted on an account provisioned through external OIDC: its
    // stored hash is the empty string because it never had a local password.
    public const string NoLocalPassword = "NO_LOCAL_PASSWORD";

    // Correct password, but the account is deactivated. Only reachable after a successful hash
    // verify (AuthService checks IsActive afterwards), so surfacing it leaks nothing the caller had
    // not already proven — unlike splitting "wrong password" from "no such account", which would
    // open the enumeration surface the timing equalizer exists to close.
    public const string AccountInactive = "ACCOUNT_INACTIVE";

    // A password change or user delete already succeeded and committed — this is NOT "your request
    // failed", it means the follow-up session revocation failed, so some of that user's existing
    // sessions may still be live. Distinct from the generic masked Internal code so the caller (and
    // the SPA) can tell "nothing happened" apart from "it happened, but isn't fully cleaned up".
    public const string SessionRevocationFailed = "SESSION_REVOCATION_FAILED";

    // A registered ISearchProvider threw SearchUnavailableException (its engine is unreachable). 503 so
    // clients and operators can tell "search is down" from a generic 500; the response message is a
    // FIXED string (DomainErrorMap.SearchUnavailableMessage) — the provider's own message, which may
    // name hosts, only goes to the Warning log.
    public const string SearchUnavailable = "SEARCH_UNAVAILABLE";

    public static string ForStatus(int status) => status switch
    {
        400 => BadUserInput,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        409 => Conflict,
        413 => PayloadTooLarge,
        429 => TooManyRequests,
        503 => SearchUnavailable,
        _ => Internal,
    };
}
