using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Struo.Application.Security;

namespace Struo.Api.Auth;

public sealed class BearerTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IUserCredentialStore store)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0) return AuthenticateResult.NoResult();

        var cred = await store.FindByAccessTokenAsync(AccessTokenHasher.Hash(token));
        if (cred is null || !cred.IsActive)
            return AuthenticateResult.Fail("Invalid token.");

        // Record last-used for leak/staleness visibility (M2). Throttled to at most once a minute so a
        // busy integration doesn't incur a DB write per request; the token itself stays permanent.
        var now = DateTime.UtcNow;
        if (cred.AccessTokenLastUsedAt is null || now - cred.AccessTokenLastUsedAt.Value > TimeSpan.FromMinutes(1))
            await store.TouchAccessTokenLastUsedAsync(cred.Id, now);

        var identity = new ClaimsIdentity(AuthSchemes.Bearer);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, cred.Id.ToString()));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), AuthSchemes.Bearer);
        return AuthenticateResult.Success(ticket);
    }
}
