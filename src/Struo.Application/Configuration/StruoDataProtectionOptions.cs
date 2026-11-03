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

    /// <summary>
    /// When true, startup fails unless <c>Certificate:Path</c> is set, in every environment. Defaults to
    /// false.
    /// </summary>
    public bool RequireKeyProtection { get; set; }

    /// <summary>The certificate that encrypts the key ring at rest, and the ones that may still decrypt it.</summary>
    public CertificateOptions Certificate { get; set; } = new();

    public sealed class CertificateOptions
    {
        /// <summary>PFX file. When set, every key written to the key ring is encrypted with it.</summary>
        public string? Path { get; set; }

        /// <summary>Password of the PFX file; blank for a file without one.</summary>
        public string? Password { get; set; }

        /// <summary>Earlier certificates, used only to decrypt keys written under them.</summary>
        public List<PreviousCertificateOptions> Previous { get; set; } = [];
    }

    public sealed class PreviousCertificateOptions
    {
        public string? Path { get; set; }

        public string? Password { get; set; }
    }
}
