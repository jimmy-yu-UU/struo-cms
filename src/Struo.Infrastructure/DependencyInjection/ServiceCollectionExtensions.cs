using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStruoInfrastructure(this IServiceCollection services)
    {
        // Fail fast. BindConfiguration + ValidateOnStart makes a missing/empty connection
        // string kill the host at boot (or at the first options materialization — e.g. when the
        // ISqlSugarClient factory below reads IOptions<DatabaseOptions>.Value) instead of surfacing
        // as a confusing 500 on the first DB access. DataAnnotations ([Required]) are enforced via
        // the BCL validator (no Microsoft.Extensions.Options.DataAnnotations package needed here).
        services.AddOptions<DatabaseOptions>()
            .BindConfiguration(DatabaseOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DataAnnotationsValidateOptions<DatabaseOptions>>();
        services.AddSingleton<ICurrentUserAccessor, StubCurrentUserAccessor>();
        services.AddSingleton<Struo.Application.Security.IPasswordHasher, Identity.Argon2idPasswordHasher>();
        services.AddScoped<Struo.Application.Security.IUserCredentialStore, Identity.SqlSugarUserCredentialStore>();
        services.AddScoped<Struo.Application.Security.IRolePermissionStore, Identity.SqlSugarRolePermissionStore>();
        services.AddScoped<Struo.Application.Security.IAuthService, Struo.Application.Security.AuthService>();
        services.AddScoped<Struo.Application.Security.IExternalUserStore, Identity.SqlSugarExternalUserStore>();
        services.AddScoped<Struo.Application.Security.IExternalLoginService, Struo.Application.Security.ExternalLoginService>();

        services.AddScoped<ISqlSugarClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DatabaseOptions>>().Value;
            var currentUser = sp.GetRequiredService<ICurrentUserAccessor>();
            return SqlSugarClientFactory.Create(options, currentUser);
        });

        return services;
    }
}
