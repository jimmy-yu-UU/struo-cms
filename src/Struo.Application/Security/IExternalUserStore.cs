namespace Struo.Application.Security;

/// <summary>Minimal user lookup/creation for external login. Deliberately bypasses the generic
/// projection (mirrors <see cref="IUserCredentialStore"/>). Returns only id + active flag.</summary>
public sealed record ExternalUserMatch(Guid Id, bool IsActive);

public interface IExternalUserStore
{
    Task<ExternalUserMatch?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Creates an active user with NO role and an empty password (never password-loginable).</summary>
    Task<Guid> CreateExternalUserAsync(string email, string? name, CancellationToken ct = default);
}
