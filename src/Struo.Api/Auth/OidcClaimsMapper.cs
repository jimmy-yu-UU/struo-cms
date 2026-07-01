using System.Security.Claims;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Normalizes an external OIDC principal into an <see cref="ExternalIdentity"/>. Reads short
/// JWT claim names (the OIDC handler is configured with MapInboundClaims = false), with fallbacks.</summary>
public static class OidcClaimsMapper
{
    public static ExternalIdentity Map(ClaimsPrincipal principal)
    {
        string? Get(params string[] keys) =>
            keys.Select(k => principal.FindFirst(k)?.Value)
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));

        var email = Get("email", ClaimTypes.Email) ?? Get("preferred_username");
        var name = Get("name", ClaimTypes.Name);
        var issuer = Get("iss") ?? "";
        var tid = Get("tid");

        var verifiedRaw = Get("email_verified");
        bool? verified = verifiedRaw is not null && bool.TryParse(verifiedRaw, out var b) ? b : null;

        return new ExternalIdentity(issuer, tid, email, verified, name);
    }
}
