// src/Struo.Api/Auth/CorsWiring.cs
namespace Struo.Api.Auth;

public static class CorsWiring
{
    public const string PolicyName = "StruoSpa";

    public static string[] ConfiguredOrigins(IConfiguration config) =>
        config.GetSection("Struo:Cors:AllowedOrigins").Get<string[]>() ?? [];

    public static bool HasConfiguredOrigins(IConfiguration config) =>
        ConfiguredOrigins(config).Length > 0;

    public static IServiceCollection AddStruoCors(this IServiceCollection services, IConfiguration config)
    {
        // Registration is unconditional so the policy is always resolvable by name (avoids depending
        // on `config` as captured at registration time — see the DI-resolved IConfiguration below).
        // The policy itself is empty (matches nothing) unless cross-origin is actually configured,
        // which keeps the default-off behavior byte-for-byte: UseStruoCors only calls app.UseCors(...)
        // when HasConfiguredOrigins(app.Configuration) is true, so an empty policy is never invoked.
        services.AddCors();
        services.Configure<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                var origins = ConfiguredOrigins(config);
                if (origins.Length == 0) return;
                policy.WithOrigins(origins)
                      .AllowCredentials()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            });
        });
        return services;
    }

    public static IApplicationBuilder UseStruoCors(this IApplicationBuilder app, IConfiguration config)
    {
        if (HasConfiguredOrigins(config)) app.UseCors(PolicyName);
        return app;
    }
}
