using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
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
        if (!AuthSchemes.HasBearerHeader(Request)) return AuthenticateResult.NoResult();

        var header = Request.Headers.Authorization.ToString();
        var token = header["Bearer ".Length..].Trim();
        if (token.Length == 0) return AuthenticateResult.NoResult();

        var cred = await store.FindByAccessTokenAsync(AccessTokenHasher.Hash(token));
        if (cred is null || !cred.IsActive)
            return AuthenticateResult.Fail("Invalid token.");

        // Record last-used for leak/staleness visibility. Throttled to at most once a minute so a
        // busy integration doesn't incur a DB write per request; the token itself stays permanent.
        var now = DateTime.UtcNow;
        if (cred.AccessTokenLastUsedAt is null || now - cred.AccessTokenLastUsedAt.Value > TimeSpan.FromMinutes(1))
            await store.TouchAccessTokenLastUsedAsync(cred.Id, now);

        var identity = new ClaimsIdentity(AuthSchemes.Bearer);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, cred.Id.ToString()));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), AuthSchemes.Bearer);
        return AuthenticateResult.Success(ticket);
    }

    // [Authorize(AuthenticationSchemes = AuthSchemes.CookieOrBearer)] challenges/forbids
    // BOTH schemes in listed order (Cookies, then Bearer). Cookie's handler (AuthWiring's
    // OnRedirectToLogin/OnRedirectToAccessDenied) now writes an error-envelope body, which starts
    // the response — the base AuthenticationHandler<TOptions> default for this handler (no override
    // previously existed) then blindly re-sets Response.StatusCode, which throws once the response
    // has already started. Guard with HasStarted so this handler's default is a no-op whenever the
    // cookie scheme already answered; the status code it would have set is already in place.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (!Context.Response.HasStarted) Context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        if (!Context.Response.HasStarted) Context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
