namespace Struo.Api.Http;

public static class ErrorCodes
{
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    // API-1: optimistic-lock (version CAS miss) split out from generic Conflict so the frontend's
    // "changed by someone else" recovery keys on this alone. Exception-driven only — the status→code
    // reverse map (ForStatus) deliberately keeps 409 → Conflict for inline ApiResults.Fail callers.
    public const string VersionConflict = "VERSION_CONFLICT";
    public const string BadUserInput = "BAD_USER_INPUT";
    public const string Validation = "VALIDATION";
    public const string Internal = "INTERNAL_SERVER_ERROR";
    // SEC-7: the app-layer login rate limiter (Program.cs, Microsoft.AspNetCore.RateLimiting)
    // writes this code directly from its OnRejected callback (no exception is thrown, so
    // DomainErrorMap is never consulted for this one).
    public const string TooManyRequests = "TOO_MANY_REQUESTS";

    public static string ForStatus(int status) => status switch
    {
        400 => BadUserInput,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        409 => Conflict,
        429 => TooManyRequests,
        _ => Internal,
    };
}
