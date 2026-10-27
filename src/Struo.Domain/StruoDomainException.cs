namespace Struo.Domain;

/// <summary>
/// Base class for a fork's own client-facing errors. The API returns <see cref="Code"/>,
/// <see cref="Status"/> and <see cref="Exception.Message"/> to the client unchanged, on REST (the error
/// envelope and the HTTP status) and on GraphQL (the <c>code</c> extension). The message is therefore
/// client-facing: it must not carry internal detail. The status must be a client error, 400 to 499; a
/// server error is not a domain error and is masked like any other unhandled exception.
/// </summary>
public abstract class StruoDomainException : Exception
{
    protected StruoDomainException(string code, int status, string message) : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfLessThan(status, 400);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(status, 499);
        Code = code;
        Status = status;
    }

    /// <summary>The stable, machine-readable error code. Prefix a fork's codes (for example <c>ACME_QUOTA_EXCEEDED</c>) so they cannot collide with the CMS's own.</summary>
    public string Code { get; }

    /// <summary>The HTTP status REST returns, 400 to 499. GraphQL keeps its HTTP 200 and carries only the code.</summary>
    public int Status { get; }
}
