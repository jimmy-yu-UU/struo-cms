namespace Struo.Application.Security;

/// <summary>
/// Resolves an external identity to a local user id, JIT-provisioning if none exists. Pure orchestration
/// (no HttpContext). Trust is anchored on the issuer/tenant (see spec §6); <c>email_verified</c> is an
/// opt-in hardening. Email matching is case-insensitive at the store layer.
/// </summary>
public sealed class ExternalLoginService(IExternalUserStore store) : IExternalLoginService
{
    public async Task<ExternalLoginResult> ResolveOrProvisionAsync(
        ExternalIdentity id, ExternalLoginPolicy policy, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(policy.AllowedTenantId) &&
            !string.Equals(id.TenantId, policy.AllowedTenantId, StringComparison.OrdinalIgnoreCase))
            return ExternalLoginResult.Fail(ExternalLoginFailure.TenantNotAllowed);

        if (policy.RequireEmailVerified && id.EmailVerified != true)
            return ExternalLoginResult.Fail(ExternalLoginFailure.EmailNotVerified);

        var email = id.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
            return ExternalLoginResult.Fail(ExternalLoginFailure.NoEmail);

        if (policy.AllowedEmailDomains.Count > 0)
        {
            var at = email.LastIndexOf('@');
            var domain = at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : "";
            if (!policy.AllowedEmailDomains.Any(d => string.Equals(d, domain, StringComparison.OrdinalIgnoreCase)))
                return ExternalLoginResult.Fail(ExternalLoginFailure.DomainNotAllowed);
        }

        var match = await store.FindByEmailAsync(email, ct);
        if (match is not null)
            return match.IsActive
                ? ExternalLoginResult.Ok(match.Id, provisioned: false)
                : ExternalLoginResult.Fail(ExternalLoginFailure.Inactive);

        var newId = await store.CreateExternalUserAsync(email, id.Name, ct);
        return ExternalLoginResult.Ok(newId, provisioned: true);
    }
}
