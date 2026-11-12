using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Warns about an enabled provider on a multi-tenant Authority whose only pin is
/// <c>AllowedEmailDomains</c>. The email's domain is a claim the identity provider's tenant owner controls, so
/// it narrows who may sign in without tying the provider to one tenant; <c>AllowedTenantId</c> does. Startup is
/// not blocked. Only provider ids are logged, never domains.</summary>
public static class OidcDomainOnlyPinWarnings
{
    public static void Log(OidcOptions options, ILogger logger)
    {
        var ids = options.EnabledProviders()
            .Where(p => p.Options.IsMultiTenantPinnedByDomainOnly())
            .Select(p => p.Id)
            .ToList();
        if (ids.Count == 0) return;

        logger.LogWarning(
            "The OIDC providers {Providers} use a multi-tenant Authority and are pinned only by " +
            "AllowedEmailDomains; set AllowedTenantId to restrict sign-in to one tenant.",
            string.Join(", ", ids));
    }
}
