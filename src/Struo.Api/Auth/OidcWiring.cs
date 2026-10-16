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
        // Fail fast at boot. When OIDC is disabled validation always passes; when enabled it
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
            .Validate(
                o => !o.Enabled || !o.IsUnpinnedMultiTenant(),
                "Oidc Authority is multi-tenant (common, organizations or consumers); " +
                "set Oidc:AllowedTenantId or Oidc:AllowedEmailDomains, or use a single-tenant Authority")
            .ValidateOnStart();
        services.PostConfigure<OidcOptions>(options => options.ApplyCollectionDefaults());

        // AddOptions above only normalizes the container-resolved copy. This second bind is a SEPARATE
        // copy read straight off IConfiguration (not through the container), and it is this local copy
        // whose Scopes feed options.Scope below — so it needs its own ApplyCollectionDefaults call; a
        // PostConfigure on the container registration alone would never touch it.
        var opts = config.GetSection(OidcOptions.SectionName).Get<OidcOptions>() ?? new OidcOptions();
        opts.ApplyCollectionDefaults();
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
                    var (principal, failure) = await ResolveLocalPrincipalAsync(ctx.HttpContext, ctx.Principal!);
                    if (principal is null)
                    {
                        ctx.Fail($"external_login_{failure}");
                        return;
                    }

                    // The handler signs THIS into the cookie SignInScheme (→ Redis ticket store).
                    ctx.Principal = principal;
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

    /// <summary>Resolves/JIT-provisions the local user for a validated external principal and returns the
    /// local Cookie principal that replaces it, revoking the session the request presented. Returns a null
    /// principal and the failure reason when the external login is refused.</summary>
    public static async Task<(ClaimsPrincipal? Principal, ExternalLoginFailure? Failure)> ResolveLocalPrincipalAsync(
        HttpContext http, ClaimsPrincipal external)
    {
        var svc = http.RequestServices.GetRequiredService<IExternalLoginService>();
        var policy = http.RequestServices.GetRequiredService<IOptions<OidcOptions>>().Value.ToPolicy();

        var identity = OidcClaimsMapper.Map(external);
        var result = await svc.ResolveOrProvisionAsync(identity, policy, http.RequestAborted);
        if (!result.Succeeded) return (null, result.Failure);

        await http.RequestServices.GetRequiredService<SessionRotation>().RevokePresentedAsync(http);

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, result.UserId!.Value.ToString()) };
        return (new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Cookie)), null);
    }
}
