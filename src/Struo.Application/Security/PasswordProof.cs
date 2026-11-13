namespace Struo.Application.Security;

/// <summary>
/// The stored password hash a login verified the password against, carried from
/// <see cref="IAuthService.AuthenticateAsync"/> to <see cref="IAuthService.ConfirmUnchangedAsync"/>.
/// Opaque outside this assembly and never printed, so it cannot reach a log, an exception message or
/// a response through the result that carries it.
/// </summary>
public sealed class PasswordProof
{
    internal PasswordProof(string storedHash) => StoredHash = storedHash;

    internal string StoredHash { get; }

    public override string ToString() => nameof(PasswordProof);
}
