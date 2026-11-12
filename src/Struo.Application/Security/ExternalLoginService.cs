namespace Struo.Application.Security;

/// <summary>
/// Resolves an external identity to a local user id, JIT-provisioning if none exists. Pure orchestration
/// (no HttpContext). Trust is anchored on the issuer/tenant; <c>email_verified</c> is an
/// opt-in hardening. The store matches an email under the database collation; a match whose own address is a
/// different spelling after normalizing is refused (<see cref="EmailAddress.IsSameAccount"/>).
/// </summary>
public sealed class ExternalLoginService(IExternalUserStore store) : IExternalLoginService
{
    public async Task<ExternalLoginResult> ResolveOrProvisionAsync(
        ExternalIdentity identity, ExternalLoginPolicy policy, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(policy.AllowedTenantId) &&
            !string.Equals(identity.TenantId, policy.AllowedTenantId, StringComparison.OrdinalIgnoreCase))
            return ExternalLoginResult.Fail(ExternalLoginFailure.TenantNotAllowed);

        if (policy.RequireEmailVerified && identity.EmailVerified != true)
            return ExternalLoginResult.Fail(ExternalLoginFailure.EmailNotVerified);

        var email = identity.Email?.Trim();
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
        {
            // The database matched this address under its collation, but the account's own address is a
            // different spelling: linking would attach the identity to an account the provider did not
            // name, and provisioning would collide with it. The login is refused instead.
            if (!EmailAddress.IsSameAccount(match.Email, email))
                return ExternalLoginResult.Fail(ExternalLoginFailure.AccountMismatch);
            return match.IsActive
                ? ExternalLoginResult.Ok(match.Id, provisioned: false)
                : ExternalLoginResult.Fail(ExternalLoginFailure.Inactive);
        }

        var newId = await store.CreateExternalUserAsync(email, identity.Name, ct);
        return ExternalLoginResult.Ok(newId, provisioned: true);
    }
}
