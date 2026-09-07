// src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Application.Search;
using Struo.Application.Security;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Security;

namespace Struo.Infrastructure.DependencyInjection;

public static class DataServiceCollectionExtensions
{
    public static IServiceCollection AddStruoData(this IServiceCollection services)
    {
        // Fail fast on out-of-range limits ([Range(1, int.MaxValue)]) at boot via
        // ValidateOnStart, enforced by the BCL DataAnnotations validator. The unwrapped singleton
        // below (StruoQueryOptions, injected directly into repositories/resolvers) is preserved.
        services.AddOptions<StruoQueryOptions>()
            .BindConfiguration(StruoQueryOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<StruoQueryOptions>, DataAnnotationsValidateOptions<StruoQueryOptions>>();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<StruoQueryOptions>>().Value);
        services.AddScoped<IPermissionService, RbacPermissionService>();
        // Expose reader and writer seams for the SAME scoped CurrentPermissions instance (same
        // request scope, same object) — if they resolved to different instances, the middleware
        // would write into one while controllers read the other and every request would see DenyAll.
        services.AddScoped<CurrentPermissions>();
        services.AddScoped<ICurrentPermissions>(sp => sp.GetRequiredService<CurrentPermissions>());
        services.AddScoped<ICurrentPermissionsWriter>(sp => sp.GetRequiredService<CurrentPermissions>());
        services.AddScoped<OrderByExpressionBuilder>();
        services.AddScoped<IItemRepository, SqlSugarItemRepository>();
        services.AddScoped<IRelationExpander, RelationExpander>();
        // Scoped to match ISqlSugarClient's lifetime (the cache is per-request).
        services.AddScoped<ILanguageProvider, LanguageProvider>();
        services.AddSingleton<IHtmlSanitizer, GanssHtmlSanitizer>();
        services.AddScoped<Struo.Application.Revisions.IRevisionStore, Struo.Infrastructure.Revisions.SqlSugarRevisionStore>();
        services.AddScoped<Struo.Application.Settings.ISiteSettingsStore, Struo.Infrastructure.Settings.SqlSugarSiteSettingsStore>();
        services.AddScoped<Struo.Application.Query.RevisionSnapshotBuilder>();
        // Search seam default: never handles a search, so the built-in LIKE path runs. TryAdd so a fork
        // that registered its own ISearchProvider BEFORE AddStruoData keeps it; one registered AFTER
        // wins anyway (last registration resolves for a single-service GetRequiredService).
        services.TryAddScoped<ISearchProvider, NullSearchProvider>();
        services.AddScoped<ItemService>();
        // Expose the use-case seam controllers depend on, forwarding to the SAME scoped
        // ItemService instance (same request scope, same object) so behavior is byte-for-byte unchanged.
        services.AddScoped<IItemUseCases>(sp => sp.GetRequiredService<ItemService>());
        return services;
    }
}
