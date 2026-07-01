namespace Struo.Application.Security;

/// <summary>Normalized external-IdP identity, after claim extraction (see Api OidcClaimsMapper).</summary>
public sealed record ExternalIdentity(
    string Issuer, string? TenantId, string? Email, bool? EmailVerified, string? Name);

/// <summary>The trust-relevant subset of OIDC config, passed to the login service.</summary>
public sealed record ExternalLoginPolicy(
    bool RequireEmailVerified, string? AllowedTenantId, IReadOnlyList<string> AllowedEmailDomains);

public enum ExternalLoginFailure { NoEmail, EmailNotVerified, DomainNotAllowed, TenantNotAllowed, Inactive }

public sealed record ExternalLoginResult(bool Succeeded, Guid? UserId, bool Provisioned, ExternalLoginFailure? Failure)
{
    public static ExternalLoginResult Ok(Guid id, bool provisioned) => new(true, id, provisioned, null);
    public static ExternalLoginResult Fail(ExternalLoginFailure failure) => new(false, null, false, failure);
}
