namespace Struo.Application.Security;

/// <summary>
/// The stored password hash a login verified the password against, carried from
/// <see cref="IAuthService.AuthenticateAsync"/> to <see cref="IAuthService.ConfirmUnchangedAsync"/>.
/// An implementation creates one with <see cref="From"/> and later asks <see cref="Matches"/> whether
/// the hash read again is the same. The hash itself has no public accessor and is never printed, so it
/// cannot reach a log, an exception message or a response through the result that carries it.
/// </summary>
public sealed class PasswordProof
{
    private readonly string _storedHash;

    private PasswordProof(string storedHash) => _storedHash = storedHash;

    /// <summary>A proof of the hash the password was verified against.</summary>
    public static PasswordProof From(string storedHash)
    {
        ArgumentNullException.ThrowIfNull(storedHash);
        return new PasswordProof(storedHash);
    }

    /// <summary>Whether <paramref name="storedHash"/> is exactly the hash this proof was made from.</summary>
    public bool Matches(string storedHash) => string.Equals(_storedHash, storedHash, StringComparison.Ordinal);

    public override string ToString() => nameof(PasswordProof);
}
