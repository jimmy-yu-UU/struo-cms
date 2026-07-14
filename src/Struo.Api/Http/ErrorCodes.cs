namespace Struo.Api.Http;

public static class ErrorCodes
{
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string BadUserInput = "BAD_USER_INPUT";
    public const string Validation = "VALIDATION";
    public const string Internal = "INTERNAL_SERVER_ERROR";

    public static string ForStatus(int status) => status switch
    {
        400 => BadUserInput,
        401 => Unauthorized,
        403 => Forbidden,
        404 => NotFound,
        409 => Conflict,
        _ => Internal,
    };
}
