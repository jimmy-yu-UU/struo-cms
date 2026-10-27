// src/Struo.Api/Hosting/StruoCmsServiceRegistration.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using Struo.Api.Auth;
using Struo.Api.GraphQl;
using Struo.Api.Http;
using Struo.Application.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Health;

namespace Struo.Api.Hosting;

/// <summary>The MVC, core-service and options registrations behind <c>AddStruoCms</c>.</summary>
internal static class StruoCmsServiceRegistration
{
    internal static void AddStruoMvc(this IServiceCollection services)
    {
        services
            .AddControllers(o => o.Filters.Add<Struo.Api.Http.EnvelopeResultFilter>())
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
                o.JsonSerializerOptions.Converters.Add(new Struo.Application.Serialization.UtcDateTimeJsonConverter());
            });

        services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
        {
            o.SuppressMapClientErrors = true;
            o.InvalidModelStateResponseFactory = ctx =>
            {
                var details = ctx.ModelState
                    .Where(kv => kv.Value is { Errors.Count: > 0 })
                    .SelectMany(kv => kv.Value!.Errors.Select(e =>
                        new Struo.Api.Http.ValidationDetail(
                            kv.Key,
                            string.IsNullOrEmpty(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)))
                    .ToList();
                return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
                    Struo.Api.Http.Envelope.Error(Struo.Api.Http.ErrorCodes.Validation,
                        "One or more validation errors occurred.", details));
            };
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<Struo.Api.Http.StruoExceptionHandler>();

        services.AddOpenApi();
    }

    internal static void AddStruoCoreServices(
        this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        services.AddStruoInfrastructure();
        // Reads Struo:ContentAssemblies from the configuration immediately, before Build() —
        // any custom configuration provider a fork adds (e.g. Key Vault) must be registered on
        // builder.Configuration before AddStruoCms() to be seen by the metadata scan.
        services.AddStruoMetadata(configuration, typeof(StruoCmsHostingExtensions).Assembly);
        services.AddStruoData();
        services.AddStruoGraphQl(environment, configuration);
        services.AddStruoFiles();
        services.AddStruoAuth(configuration, environment);
        services.AddStruoCors(configuration);
        services.AddStruoForwardedHeaders(configuration);
        services.AddStruoOidc(configuration);
    }

    internal static void AddStruoOptions(this IServiceCollection services)
    {
        services.AddOptions<Struo.Application.Configuration.BrandingOptions>()
            .BindConfiguration(Struo.Application.Configuration.BrandingOptions.SectionName);
        // Config-bound tuning for the per-client-IP login rate limiter in StruoCmsRateLimiting (defaults:
        // disabled, 5 attempts / 60s if enabled) — see LoginRateLimitOptions for why it ships off
        services.AddOptions<Struo.Application.Configuration.LoginRateLimitOptions>()
            .BindConfiguration(Struo.Application.Configuration.LoginRateLimitOptions.SectionName);
        // Config-bound tuning for the per-account login throttle (AuthController.Login, via
        // ILoginAttemptThrottle) — independent of the per-client-IP limiter above. Defaults: enabled,
        // 10 attempts per 900s
        services.AddOptions<Struo.Application.Configuration.LoginAccountRateLimitOptions>()
            .BindConfiguration(Struo.Application.Configuration.LoginAccountRateLimitOptions.SectionName);
        // Process-wide cap on concurrent logins (see LoginConcurrencyOptions and the global limiter in
        // StruoCmsRateLimiting).
        services.AddOptions<Struo.Application.Configuration.LoginConcurrencyOptions>()
            .BindConfiguration(Struo.Application.Configuration.LoginConcurrencyOptions.SectionName);
        // Fail fast at boot: MinLength is published to the SPA and sizes the admin password generator, so
        // a misconfigured MinLength > MaxLength would hand an administrator a "Generate strong password"
        // button that produces passwords the server always rejects, with no error until the next write.
        services.AddOptions<Struo.Application.Configuration.PasswordPolicyOptions>()
            .BindConfiguration(Struo.Application.Configuration.PasswordPolicyOptions.SectionName)
            .Validate(
                o => o.MinLength >= 1 && o.MinLength <= o.MaxLength,
                "Auth:Password:MinLength must be >= 1 and <= Auth:Password:MaxLength.")
            .ValidateOnStart();
        services.AddOptions<Struo.Application.Configuration.PasswordRateLimitOptions>()
            .BindConfiguration(Struo.Application.Configuration.PasswordRateLimitOptions.SectionName);
        services.AddOptions<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions>(AuthSchemes.Cookie)
            .PostConfigure<DistributedCacheTicketStore>((options, store) => options.SessionStore = store);
        services.AddScoped<SchemaService>();
        // /api/config is anonymous. Responses are cached with a short TTL (ConfigController) and
        // evicted immediately on a branding save (SettingsController).
        services.AddMemoryCache();
        services.AddHealthChecks()
            .AddCheck<DbReadinessCheck>("database", tags: ["ready"])
            .AddCheck<Struo.Infrastructure.Health.CacheReadinessCheck>("cache", tags: ["ready"]);
    }
}
