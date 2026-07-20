namespace Struo.Application.Configuration;

/// <summary>Config-bound branding shown by the admin UI (login page, shell topbar, tab title).
/// Bound from the <c>Branding</c> configuration section. Defaults keep the stock "StruoCMS" identity.
/// The write side (in-app editing) is a future Site-Settings phase; this is deploy-time config only.</summary>
public sealed class BrandingOptions
{
    public const string SectionName = "Branding";

    public string Name { get; set; } = "StruoCMS";
    public string? LogoUrl { get; set; }
}
