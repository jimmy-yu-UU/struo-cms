namespace Struo.Application.Security;

public enum AuthFailure { InvalidCredentials, Inactive }

public sealed record AuthResult(Guid? UserId, AuthFailure? Failure)
{
    public bool Succeeded => UserId is not null;
    public static AuthResult Ok(Guid id) => new(id, null);
    public static AuthResult Fail(AuthFailure f) => new(null, f);
}

public interface IAuthService
{
    Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken ct = default);
}
