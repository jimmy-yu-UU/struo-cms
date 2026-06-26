// src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Application.Security;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Security;

namespace Struo.Infrastructure.DependencyInjection;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddStruoData(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<StruoQueryOptions>(configuration.GetSection(StruoQueryOptions.SectionName));
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<StruoQueryOptions>>().Value);
        services.AddSingleton<IPermissionService, AllowAllPermissionService>();
        services.AddScoped<IItemRepository, SqlSugarItemRepository>();
        services.AddScoped<IRelationExpander, RelationExpander>();
        services.AddScoped<IRelationFilterResolver, RelationFilterResolver>();
        // Scoped to match ISqlSugarClient's lifetime (the cache is per-request).
        services.AddScoped<ILanguageProvider, LanguageProvider>();
        services.AddScoped<ItemService>();
        return services;
    }
}
