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

    /// <summary>The scopes applied by <see cref="ApplyCollectionDefaults"/> when none are configured.
    /// Held as a named default rather than a property initializer because ConfigurationBinder APPENDS
    /// bound array elements to a non-empty collection default instead of replacing it, which made
    /// narrowing this list impossible.</summary>
    public static readonly string[] DefaultScopes = ["openid", "email", "profile"];

    public string[] Scopes { get; set; } = [];
    public string ReturnUrlDefault { get; set; } = "/";
    // An external identity links to a local account by email equality, so these guards pin trust.
    // They default to off, except that an enabled multi-tenant Authority needs a tenant or domain pin
    // (see IsUnpinnedMultiTenant). See docs/guide/en/16-authentication.md.
    public bool RequireEmailVerified { get; set; }
    public string? AllowedTenantId { get; set; }
    public string[] AllowedEmailDomains { get; set; } = [];

    private static readonly string[] MultiTenantSegments = ["common", "organizations", "consumers"];

    /// <summary>True when <see cref="Authority"/> is an Entra multi-tenant endpoint (a path segment of
    /// <c>common</c>, <c>organizations</c> or <c>consumers</c>) and neither a tenant nor an email
    /// domain is pinned.</summary>
    public bool IsUnpinnedMultiTenant()
    {
        if (!Uri.TryCreate(Authority, UriKind.Absolute, out var uri)) return false;
        var multiTenant = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Any(s => MultiTenantSegments.Contains(s, StringComparer.OrdinalIgnoreCase));
        if (!multiTenant) return false;
        var pinned = !string.IsNullOrWhiteSpace(AllowedTenantId)
                     || AllowedEmailDomains.Any(d => !string.IsNullOrWhiteSpace(d));
        return !pinned;
    }

    public ExternalLoginPolicy ToPolicy() =>
        new(RequireEmailVerified, AllowedTenantId, AllowedEmailDomains);

    /// <summary>Applies defaults that cannot live in a property initializer without
    /// ConfigurationBinder appending to them. Must run after binding and before the value is
    /// consumed; both of <c>OidcWiring.AddStruoOidc</c>'s binds call this.</summary>
    public void ApplyCollectionDefaults()
    {
        if (Scopes.Length == 0) Scopes = [.. DefaultScopes];
    }
}
