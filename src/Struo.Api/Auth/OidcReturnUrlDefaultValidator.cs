using Microsoft.Extensions.Options;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Fails startup when <c>Oidc:ReturnUrlDefault</c> is neither a site-relative path nor an absolute
/// URL whose origin is listed in <c>Struo:Cors:AllowedOrigins</c>.</summary>
public sealed class OidcReturnUrlDefaultValidator(IConfiguration config) : IValidateOptions<OidcOptions>
{
    public ValidateOptionsResult Validate(string? name, OidcOptions options) =>
        LocalRedirect.IsAllowedDestination(options.ReturnUrlDefault, CorsWiring.ConfiguredOrigins(config))
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                "Oidc:ReturnUrlDefault must be a site-relative path starting with '/', or an absolute URL whose origin " +
                "(scheme, host, port) is listed in Struo:Cors:AllowedOrigins");
}
