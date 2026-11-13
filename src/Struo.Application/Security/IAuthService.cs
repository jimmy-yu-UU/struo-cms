namespace Struo.Application.Security;

public enum AuthFailure { InvalidCredentials, Inactive }

public sealed record AuthResult(Guid? UserId, AuthFailure? Failure, PasswordProof? Proof = null)
{
    public bool Succeeded => UserId is not null;
    public static AuthResult Ok(Guid id, PasswordProof? proof = null) => new(id, null, proof);
    public static AuthResult Fail(AuthFailure f) => new(null, f);
}

public interface IAuthService
{
    Task<AuthResult> AuthenticateAsync(string email, string password, CancellationToken ct = default);

    /// <summary>
    /// Re-reads the user after a successful <see cref="AuthenticateAsync"/> and says whether the
    /// credential that was verified is still the stored one: the user exists, is active, and the stored
    /// password hash equals the one the password was verified against. A login calls this once its
    /// session is written; <c>false</c> means a password change or a removal landed in between, so the
    /// session must be revoked. A result without a <see cref="PasswordProof"/> never confirms.
    /// </summary>
    Task<bool> ConfirmUnchangedAsync(AuthResult result, CancellationToken ct = default);
}
