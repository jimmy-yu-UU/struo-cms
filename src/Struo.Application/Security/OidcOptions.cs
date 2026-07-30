namespace Struo.Application.Security;

/// <summary>Config-bound OIDC settings. Bound from the <c>Oidc</c> configuration section.
/// <c>ClientSecret</c> comes from env / user-secrets, never committed.</summary>
public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    public bool Enabled { get; set; }
    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string CallbackPath { get; set; } = "/signin-oidc";
    public string[] Scopes { get; set; } = ["openid", "email", "profile"];
    public string ReturnUrlDefault { get; set; } = "/";
    // ACCEPTED RISK: JIT provisioning links an external identity to an existing
    // local account by email equality, and these guards default to OFF. That means a deployment MUST
    // pin trust via configuration — set a single-tenant Authority + AllowedTenantId (and/or
    // AllowedEmailDomains), and prefer RequireEmailVerified=true — otherwise a password account could be
    // taken over by any IdP identity presenting a matching email. Left open by decision to keep the
    // zero-config dev experience; production is expected to constrain it. See
    // docs/guide/en/12-auth-and-rbac.md.
    public bool RequireEmailVerified { get; set; }
    public string? AllowedTenantId { get; set; }
    public string[] AllowedEmailDomains { get; set; } = [];

    public ExternalLoginPolicy ToPolicy() =>
        new(RequireEmailVerified, AllowedTenantId, AllowedEmailDomains);
}
