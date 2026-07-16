using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Struo.Application.Security;

namespace Struo.Api.Auth;

/// <summary>Registers the external OIDC scheme when configured (else a no-op, so password login is
/// unaffected). Binds <see cref="OidcOptions"/>. On a validated token it resolves/JIT-provisions a
/// local user and replaces the principal with a Cookie identity so the existing cookie SignInScheme
/// (Redis-backed) persists the session. Must be called AFTER AddStruoAuth (which registers the
/// Cookie scheme used as SignInScheme).</summary>
public static class OidcWiring
{
    public static IServiceCollection AddStruoOidc(this IServiceCollection services, IConfiguration config)
    {
        // ARC-5: fail fast at boot. When OIDC is disabled validation always passes; when enabled it
        // requires Authority/ClientId/ClientSecret to be present (a half-configured external login
        // would otherwise only fail at the first sign-in attempt).
        services.AddOptions<OidcOptions>()
            .BindConfiguration(OidcOptions.SectionName)
            .Validate(
                o => !o.Enabled ||
                     (!string.IsNullOrWhiteSpace(o.Authority)
                      && !string.IsNullOrWhiteSpace(o.ClientId)
                      && !string.IsNullOrWhiteSpace(o.ClientSecret)),
                "Oidc enabled requires Authority, ClientId, ClientSecret")
            .ValidateOnStart();

        var opts = config.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();
        if (!opts.Enabled || string.IsNullOrWhiteSpace(opts.Authority))
            return services; // scheme not registered → /api/auth/login/oidc returns 404

        services.AddAuthentication().AddOpenIdConnect(AuthSchemes.Oidc, options =>
        {
            options.Authority = opts.Authority;
            options.ClientId = opts.ClientId;
            options.ClientSecret = opts.ClientSecret;
            options.CallbackPath = opts.CallbackPath;
            options.ResponseType = "code";
            options.UsePkce = true;
            options.SaveTokens = false;
            options.MapInboundClaims = false;          // keep short JWT claim names for OidcClaimsMapper
            options.SignInScheme = AuthSchemes.Cookie;  // the handler persists our rebuilt principal here
            options.GetClaimsFromUserInfoEndpoint = true;

            options.Scope.Clear();
            foreach (var s in opts.Scopes)
                options.Scope.Add(s);

            options.Events = new OpenIdConnectEvents
            {
                OnTokenValidated = async ctx =>
                {
                    var svc = ctx.HttpContext.RequestServices.GetRequiredService<IExternalLoginService>();
                    var policy = ctx.HttpContext.RequestServices
                        .GetRequiredService<IOptions<OidcOptions>>().Value.ToPolicy();

                    var identity = OidcClaimsMapper.Map(ctx.Principal!);
                    var result = await svc.ResolveOrProvisionAsync(identity, policy, ctx.HttpContext.RequestAborted);

                    if (!result.Succeeded)
                    {
                        ctx.Fail($"external_login_{result.Failure}");
                        return;
                    }

                    // Replace the external principal with a local Cookie identity; the handler then
                    // signs THIS into the cookie SignInScheme (→ Redis ticket store).
                    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()) };
                    ctx.Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Cookie));
                },

                OnRemoteFailure = ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return ctx.Response.WriteAsJsonAsync(new { error = new { message = "External login failed." } });
                },

                OnAccessDenied = ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return ctx.Response.WriteAsJsonAsync(new { error = new { message = "External login denied." } });
                }
            };
        });

        return services;
    }
}
