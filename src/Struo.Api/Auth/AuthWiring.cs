using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Struo.Application.Abstractions;

namespace Struo.Api.Auth;

public static class AuthWiring
{
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
                options.Cookie.Name = "struo.session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = securePolicy;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                // API, not MVC views: return 401/403 instead of redirecting.
                options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, BearerTokenAuthenticationHandler>(
                AuthSchemes.Bearer, _ => { });

        services.AddAuthorization();
        return services;
    }
}
