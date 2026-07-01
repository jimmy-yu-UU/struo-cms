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
    public bool RequireEmailVerified { get; set; }
    public string? AllowedTenantId { get; set; }
    public string[] AllowedEmailDomains { get; set; } = [];

    public ExternalLoginPolicy ToPolicy() =>
        new(RequireEmailVerified, AllowedTenantId, AllowedEmailDomains);
}
