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
        // ARC-5: fail fast on out-of-range limits ([Range(1, int.MaxValue)]) at boot via
        // ValidateOnStart, enforced by the BCL DataAnnotations validator. The unwrapped singleton
        // below (StruoQueryOptions, injected directly into repositories/resolvers) is preserved.
        services.AddOptions<StruoQueryOptions>()
            .BindConfiguration(StruoQueryOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StruoQueryOptions>, DataAnnotationsValidateOptions<StruoQueryOptions>>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<StruoQueryOptions>>().Value);
        services.AddScoped<IPermissionService, RbacPermissionService>();
        services.AddScoped<ICurrentPermissions, CurrentPermissions>();
        services.AddScoped<OrderByExpressionBuilder>();
        services.AddScoped<IItemRepository, SqlSugarItemRepository>();
        services.AddScoped<IRelationExpander, RelationExpander>();
        services.AddScoped<IRelationFilterResolver, RelationFilterResolver>();
        // Scoped to match ISqlSugarClient's lifetime (the cache is per-request).
        services.AddScoped<ILanguageProvider, LanguageProvider>();
        services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>();
        services.AddScoped<Struo.Application.Revisions.IRevisionStore, Struo.Infrastructure.Revisions.SqlSugarRevisionStore>();
        services.AddScoped<Struo.Application.Query.RevisionSnapshotBuilder>();
        services.AddScoped<ItemService>();
        return services;
    }
}
