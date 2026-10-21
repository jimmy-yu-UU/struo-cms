namespace Struo.Application.Security;

/// <summary>Settings of one OIDC provider, bound from <c>Oidc:Providers:{id}</c>.
/// <c>ClientSecret</c> comes from env / user-secrets, never committed.</summary>
public sealed class OidcProviderOptions
{
    public bool Enabled { get; set; }

    /// <summary>Label of the provider's sign-in button.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Icon token the admin UI maps to a glyph; blank shows no icon.</summary>
    public string? Icon { get; set; }

    /// <summary>Sort key of the sign-in buttons, ascending; ties fall back to the provider id.
    /// IConfiguration enumerates keys alphabetically, so the file order cannot be relied on.</summary>
    public int Order { get; set; }

    public string? Authority { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>The path the provider redirects back to; blank means <c>/signin-oidc-{id}</c>.</summary>
    public string? CallbackPath { get; set; }

    /// <summary>The scopes applied by <see cref="ApplyCollectionDefaults"/> when none are configured.
    /// Held as a named default rather than a property initializer because ConfigurationBinder APPENDS
    /// bound array elements to a non-empty collection default instead of replacing it, which made
    /// narrowing this list impossible.</summary>
    public static readonly string[] DefaultScopes = ["openid", "email", "profile"];

    public string[] Scopes { get; set; } = [];

    /// <summary>The prompt values an authorize request may carry.</summary>
    public static readonly string[] AllowedPromptValues = ["select_account", "login", "consent", "none"];

    /// <summary>Value of the authorize request's <c>prompt</c> parameter: one or more space-separated
    /// entries of <see cref="AllowedPromptValues"/>. Blank sends no prompt.</summary>
    public string? Prompt { get; set; } = "select_account";

    // An external identity links to a local account by email equality, so these guards pin trust.
    // They default to off, except that an enabled multi-tenant Authority needs a tenant or domain pin
    // (see IsUnpinnedMultiTenant). See docs/guide/en/16-authentication.md.
    public bool RequireEmailVerified { get; set; }
    public string? AllowedTenantId { get; set; }
    public string[] AllowedEmailDomains { get; set; } = [];

    /// <summary>The configured <see cref="CallbackPath"/>, or <c>/signin-oidc-{id}</c> when blank.</summary>
    public string EffectiveCallbackPath(string id) =>
        string.IsNullOrWhiteSpace(CallbackPath) ? $"/signin-oidc-{id}" : CallbackPath;

    /// <summary>True when <see cref="Prompt"/> is blank or every space-separated entry is allowed.</summary>
    public bool HasValidPrompt() =>
        string.IsNullOrWhiteSpace(Prompt)
        || Prompt.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(p => AllowedPromptValues.Contains(p, StringComparer.Ordinal));

    // The GUID is the Entra consumers tenant, which accepts any personal Microsoft account.
    private static readonly string[] MultiTenantSegments =
        ["common", "organizations", "consumers", "9188040d-6c67-4c5b-b112-36a304b66dad"];

    /// <summary>True when <see cref="Authority"/> is an Entra multi-tenant endpoint (a path segment of
    /// <c>common</c>, <c>organizations</c>, <c>consumers</c> or the consumers tenant GUID) and neither a tenant nor an email
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
