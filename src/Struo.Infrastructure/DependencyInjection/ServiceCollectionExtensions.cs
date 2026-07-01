using Microsoft.Extensions.Configuration;
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
    public static IServiceCollection AddStruoInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
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
