// src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Struo.Application.Changes;
using Struo.Application.Configuration;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Application.Search;
using Struo.Application.Security;
using Struo.Infrastructure.Changes;
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
        // Seed source for the Language table (LanguageSeeder). Validated at boot alongside the other
        // options so a misconfigured section fails the host, not the first seeding run.
        services.AddOptions<LocalizationOptions>()
            .BindConfiguration(LocalizationOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LocalizationOptions>, LocalizationOptionsValidator>();
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
        // Write-side change notifications (U5b). The notifier is always registered; listeners are not —
        // a fork adds `services.AddScoped<IItemChangeListener, MyIndexer>()` (any number). With no
        // listener registered the notifier returns immediately. TryAdd so a fork that registers its own
        // IItemChangeNotifier BEFORE AddStruoData keeps it; one registered AFTER wins anyway (last
        // registration resolves for a single-service GetRequiredService) — either way it must keep the
        // never-throw contract IItemChangeNotifier documents.
        services.TryAddScoped<IItemChangeNotifier, ItemChangeNotifier>();
        services.AddScoped<ItemService>();
        // Expose the use-case seam controllers depend on, forwarding to the SAME scoped
        // ItemService instance (same request scope, same object) so behavior is byte-for-byte unchanged.
        services.AddScoped<IItemUseCases>(sp => sp.GetRequiredService<ItemService>());
        return services;
    }
}
