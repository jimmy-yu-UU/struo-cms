using Microsoft.Extensions.Options;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Fails startup when <c>Oidc:ReturnUrlDefault</c> is an absolute URL whose origin is not listed
/// in <c>Struo:Cors:AllowedOrigins</c>. A value that is not absolute is not checked here.</summary>
public sealed class OidcReturnUrlDefaultValidator(IConfiguration config) : IValidateOptions<OidcOptions>
{
    public ValidateOptionsResult Validate(string? name, OidcOptions options)
    {
        var value = options.ReturnUrlDefault;
        if (!Uri.TryCreate(value, UriKind.Absolute, out _)
            || LocalRedirect.IsValidDefault(value, CorsWiring.ConfiguredOrigins(config)))
            return ValidateOptionsResult.Success;
        return ValidateOptionsResult.Fail(
            "Oidc:ReturnUrlDefault is an absolute URL; its origin (scheme, host, port) must be listed in Struo:Cors:AllowedOrigins");
    }
}
