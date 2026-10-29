namespace Struo.Application.Configuration;

/// <summary>
/// Config-bound settings for the ASP.NET Core DataProtection key ring, which protects the authentication
/// cookie, the antiforgery tokens and the OpenID Connect correlation cookies. Bound from the
/// <c>DataProtection</c> configuration section.
/// </summary>
public sealed class StruoDataProtectionOptions
{
    public const string SectionName = "DataProtection";

    /// <summary>
    /// The application discriminator. Replicas that share a key ring must use the same name, or each
    /// replica rejects what another one protected. Must not be blank. Defaults to <c>StruoCMS</c>.
    /// </summary>
    public string ApplicationName { get; set; } = "StruoCMS";
}
