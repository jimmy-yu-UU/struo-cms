namespace Struo.Application.Security;

/// <summary>Credential lookups that deliberately bypass the generic projection so password
/// hashes never traverse the read path.</summary>
public sealed record UserCredential(
    Guid Id, string PasswordEncoded, bool IsActive, DateTime? AccessTokenLastUsedAt = null);

public interface IUserCredentialStore
{
    Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default);

    /// <summary>Records that the given user's access token was just used. Callers throttle how often
    /// they invoke this (last-used is observability, not per-request accounting).</summary>
    Task TouchAccessTokenLastUsedAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default);
}
