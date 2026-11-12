namespace Struo.Application.Security;

/// <summary>Credential lookups that deliberately bypass the generic projection so password
/// hashes never traverse the read path.</summary>
public sealed record UserCredential(
    Guid Id, string PasswordEncoded, bool IsActive, string Email, DateTime? AccessTokenLastUsedAt = null);

public interface IUserCredentialStore
{
    /// <summary>The row the database matches to <paramref name="email"/> under its collation, which can be a
    /// different spelling than the one given; <see cref="UserCredential.Email"/> carries the stored one.
    /// <see cref="EmailAddress.IsSameAccount"/> decides whether a login may use the row.</summary>
    Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Credential lookup by id, for the self-service password change's proof-of-knowledge
    /// check. Here rather than on <see cref="IUserAccountStore"/> because it hands back a password
    /// hash, which is exactly what this interface exists to keep off the generic read path.</summary>
    Task<UserCredential?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Records that the given user's access token was just used. Callers throttle how often
    /// they invoke this (last-used is observability, not per-request accounting).</summary>
    Task TouchAccessTokenLastUsedAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default);
}
