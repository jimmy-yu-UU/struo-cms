namespace Struo.Application.Security;

/// <summary>An enabled provider and the id it is configured under.</summary>
public sealed record OidcProvider(string Id, OidcProviderOptions Options);

/// <summary>Config-bound OIDC settings. Bound from the <c>Oidc</c> configuration section: one entry
/// per external identity provider under <c>Oidc:Providers:{id}</c>.</summary>
public sealed class OidcOptions
{
    public const string SectionName = "Oidc";

    /// <summary>Provider ids are lowercase letters, digits and hyphens: they appear in URLs and scheme names.</summary>
    public const string ProviderIdPattern = "^[a-z0-9-]+$";

    /// <summary>Where a successful sign-in lands when the request carried no usable <c>returnUrl</c>.</summary>
    public string ReturnUrlDefault { get; set; } = "/";

    public Dictionary<string, OidcProviderOptions> Providers { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The enabled providers, sorted by <see cref="OidcProviderOptions.Order"/> then id.</summary>
    public IReadOnlyList<OidcProvider> EnabledProviders() =>
        [.. Providers
            .Where(p => p.Value.Enabled)
            .OrderBy(p => p.Value.Order)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new OidcProvider(p.Key, p.Value))];

    /// <summary>Looks up an enabled provider by id; false for an unknown or disabled one.</summary>
    public bool TryGetEnabled(string id, out OidcProviderOptions options)
    {
        if (Providers.TryGetValue(id, out var found) && found.Enabled)
        {
            options = found;
            return true;
        }
        options = null!;
        return false;
    }

    /// <summary>Applies each provider's collection defaults; see
    /// <see cref="OidcProviderOptions.ApplyCollectionDefaults"/>.</summary>
    public void ApplyCollectionDefaults()
    {
        foreach (var provider in Providers.Values) provider.ApplyCollectionDefaults();
    }
}
