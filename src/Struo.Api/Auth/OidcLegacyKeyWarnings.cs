namespace Struo.Api.Auth;

/// <summary>Warns about the flat <c>Oidc</c> keys of the single-provider shape. The binder ignores them, so a
/// configuration carried over from an earlier version would otherwise lose its SSO without a word. Only key
/// names are logged, never values.</summary>
public static class OidcLegacyKeyWarnings
{
    private static readonly string[] LegacyKeys =
    [
        "Enabled", "Authority", "ClientId", "ClientSecret", "CallbackPath", "Scopes", "Prompt",
        "AllowedTenantId", "AllowedEmailDomains", "RequireEmailVerified",
    ];

    public static void Log(IConfiguration configuration, ILogger logger)
    {
        var found = LegacyKeys
            .Select(key => $"Oidc:{key}")
            .Where(key => configuration.GetSection(key).Exists())
            .ToList();
        if (found.Count == 0) return;

        logger.LogWarning(
            "The configuration keys {Keys} are ignored: SSO providers are configured under Oidc:Providers:<id>.",
            string.Join(", ", found));
    }
}
