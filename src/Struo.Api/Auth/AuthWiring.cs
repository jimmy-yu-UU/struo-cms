using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Struo.Api.Http;
using Struo.Application.Abstractions;

namespace Struo.Api.Auth;

public static class AuthWiring
{
    // [Authorize]-attribute-level challenges run inside the cookie handler, before MVC
    // gets a chance to run — so EnvelopeResultFilter never sees these responses and they used to
    // go out with an empty body, unlike in-action PermissionDeniedException 401/403s (mapped by
    // DomainErrorMap) which DO carry an envelope. OnRedirectToLogin/OnRedirectToAccessDenied below
    // write the same envelope shape directly, using the shared camelCase options (see
    // EnvelopeJsonOptionsHolder) rather than relying on DI to already agree.
    public static IServiceCollection AddStruoAuth(this IServiceCollection services, IConfiguration config, IWebHostEnvironment env)
    {
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<ICurrentUserAccessor, HttpContextCurrentUserAccessor>());

        // Session store backend: real Redis when configured, in-memory otherwise (tests/dev fallback).
        var redis = config.GetValue<string>("Redis:ConnectionString");
        if (!string.IsNullOrWhiteSpace(redis))
            services.AddStackExchangeRedisCache(o => o.Configuration = redis);
        else
            services.AddDistributedMemoryCache();

        services.AddSingleton<DistributedCacheTicketStore>();

        // In production HTTPS is enforced at the reverse proxy; in development/test the test host
        // runs over HTTP so SecurePolicy.Always would prevent the cookie from being sent back.
        var securePolicy = env.IsProduction()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

        services.AddAuthentication(AuthSchemes.Cookie)
            .AddCookie(AuthSchemes.Cookie, options =>
            {
                options.Cookie.Name = AuthSchemes.SessionCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = securePolicy;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                // API, not MVC views: return 401/403 with an error envelope instead of redirecting.
                // Body added alongside the pre-existing status-code-only behavior (which is
                // preserved verbatim) so attribute-level challenges match the in-action error shape.
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return ctx.Response.WriteAsJsonAsync(
                        Envelope.Error(Struo.Api.Http.ErrorCodes.Unauthorized, "Authentication required."),
                        EnvelopeJsonOptionsHolder.Instance);
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return ctx.Response.WriteAsJsonAsync(
                        Envelope.Error(Struo.Api.Http.ErrorCodes.Forbidden, "Forbidden."),
                        EnvelopeJsonOptionsHolder.Instance);
                };
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(
                AuthSchemes.Bearer, _ => { });

        // Cross-origin mode (SPA on a different origin) requires SameSite=None, which browsers
        // only honor alongside Secure — so cross-origin also forces the secure policy on.
        // Resolved lazily via IConfiguration from DI (not the `config` parameter captured above)
        // so that test hosts which append configuration after service registration (e.g.
        // WebApplicationFactory.ConfigureAppConfiguration) are still honored: CookieAuthenticationOptions
        // are materialized per-request by the auth handler, long after the app has fully built.
        services.AddOptions<CookieAuthenticationOptions>(AuthSchemes.Cookie)
            .Configure<IConfiguration>((options, resolvedConfig) =>
            {
                if (!CorsWiring.HasConfiguredOrigins(resolvedConfig)) return;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.None;
            });

        services.AddAuthorization();

        // FilesController's file-collection RBAC decisions, extracted off its constructor.
        // Scoped so it shares the per-request ICurrentPermissions/IPermissionService instances.
        services.AddScoped<IFileAccessPolicy, FileAccessPolicy>();

        return services;
    }
}
