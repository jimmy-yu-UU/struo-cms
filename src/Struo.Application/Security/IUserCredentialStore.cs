namespace Struo.Application.Security;

/// <summary>Credential lookups that deliberately bypass the generic projection so password
/// hashes never traverse the read path.</summary>
public sealed record UserCredential(Guid Id, string PasswordEncoded, bool IsActive);

public interface IUserCredentialStore
{
    Task<UserCredential?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<UserCredential?> FindByAccessTokenAsync(string tokenHash, CancellationToken ct = default);
}
