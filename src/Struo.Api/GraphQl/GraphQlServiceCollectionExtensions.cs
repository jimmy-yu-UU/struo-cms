// src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs
using HotChocolate.AspNetCore;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Struo.Application.Configuration;

namespace Struo.Api.GraphQl;

public static class GraphQlServiceCollectionExtensions
{
    /// <summary>
    /// Resolves whether this instance may disclose its schema. Shared by the introspection gate below
    /// and the <c>?sdl</c> gate in <see cref="MapStruoGraphQl"/> so the two cannot drift apart — they
    /// are one concern, and their drift is exactly what left <c>?sdl</c> serving the full schema
    /// anonymously in Production while introspection was correctly refused. Null/absent config means
    /// Development-only, which is the shipped behavior.
    /// </summary>
    internal static bool ResolveExposeSchema(IConfiguration config, IHostEnvironment env) =>
        config.GetSection(GraphQlOptions.SectionName)
            .GetValue<bool?>(nameof(GraphQlOptions.ExposeSchema))
        ?? env.IsDevelopment();

    public static IServiceCollection AddStruoGraphQl(
        this IServiceCollection services, IHostEnvironment env, IConfiguration config)
    {
        services.AddOptions<GraphQlOptions>().BindConfiguration(GraphQlOptions.SectionName);
        var exposeSchema = ResolveExposeSchema(config, env);
        // Registered on the application IServiceCollection (not chained via
        // IRequestExecutorBuilder.AddErrorFilter<T>()): the schema-services container HotChocolate
        // builds for the operation-execution pipeline is a filtered subset of app services that
        // excludes logging, so constructor-injecting ILogger<StruoErrorFilter> via schema-scoped
        // type-activation fails ("Unable to resolve service for type ILogger<StruoErrorFilter>").
        // The IServiceCollection-level AddErrorFilter overload registers against application
        // services instead, where full constructor DI (including ILogger<T>) resolves normally.
        services.AddErrorFilter<StruoErrorFilter>();
        services.AddScoped<IGraphQlDataSource, ItemServiceGraphQlDataSource>();
        // AddTypeModule<T>() resolves T via GetRequiredService<T>() against application services
        // (not schema-scoped activation) — StruoTypeModule must be registered explicitly.
        services.AddSingleton<StruoTypeModule>();

        services
            .AddGraphQLServer()
            .AddQueryType(d => d
                .Name("Query")
                // Anchor field so the schema is always valid even before the type module adds
                // collection fields (GraphQL requires Query to have >=1 field).
                .Field("_service").Type<StringType>().Resolve(_ => "StruoCMS GraphQL"))
            .AddMutationType(d => d
                .Name("Mutation")
                // Anchor field so the Mutation root is valid even before the type module adds
                // collection mutations (GraphQL requires each root type to have >=1 field). Also
                // serves as the empty-schema guard when zero collections are discovered.
                .Field("_service").Type<StringType>().Resolve(_ => "StruoCMS GraphQL mutations"))
            .AddType<LongType>()
            .AddType<DateTimeType>()
            .AddType<DateType>()
            .AddType<UuidType>()
            .AddType<AnyType>()            // JSON scalar (SDL name "Any")
            .AddJsonTypeConverter()        // lets resolvers return dictionaries/JsonElement for Any
            .AddTypeModule<StruoTypeModule>() // dynamic per-collection object/list/filter types + root query fields
            .AddMaxExecutionDepthRule(12, skipIntrospectionFields: true)
            // Static cost analysis is the alias-amplification defense — HotChocolate 16.4.0 has
            // no dedicated alias/operation-count rule, but every aliased selection accrues its own
            // field cost, so a request that repeats an expensive list field under N aliases costs ~N×
            // and is rejected before any resolver (and thus any DB call) runs. This complements the
            // max-execution-depth rule and the accepted-absent rate limiting.
            .AddCostAnalyzer()
            .ModifyCostOptions(o =>
            {
                o.EnforceCostLimits = true;
                // Calibrated empirically against the full GraphQL suite (see GraphQlCostAnalysisTests):
                //   - heaviest LEGITIMATE query (3-root articles/categories/tags, the concurrency
                //     smoke test) measures fieldCost = 33;
                //   - a 50-alias `articles { items { id } }` amplification measures fieldCost = 550.
                // 150 sits between them (~4.5x headroom over legitimate traffic, rejects the bomb at
                // ~0.27x). The default 1000-tier would NOT catch a cheap-field alias bomb (550 < 1000),
                // so it is deliberately lowered. Note the HotChocolate 16.4.0 default MaxFieldCost is
                // 1000; this is the smallest round value that separates our observed legit/abuse costs.
                o.MaxFieldCost = 150.0;
                o.MaxTypeCost = 150.0;
            })
            .DisableIntrospection(!exposeSchema)
            // Pin both root scopes to Request explicitly (Mutation would otherwise fall back to
            // HotChocolate's implicit default) so resolvers — query AND mutation — resolve scoped
            // services against the HTTP request's DI scope. That's what lets ItemService's RBAC
            // checks see the per-request ICurrentPermissions snapshot populated by
            // PermissionResolutionMiddleware; without this, a mutation resolver could resolve a
            // different scoped ItemService with no (or stale) permission snapshot attached.
            .ModifyOptions(o =>
            {
                o.DefaultQueryDependencyInjectionScope = DependencyInjectionScope.Request;
                o.DefaultMutationDependencyInjectionScope = DependencyInjectionScope.Request;
            });

        return services;
    }

    public static void MapStruoGraphQl(this WebApplication app)
    {
        var exposeSchema = ResolveExposeSchema(app.Configuration, app.Environment);
        app.MapGraphQL("/graphql")
            .WithOptions(options =>
            {
                options.Tool.Enable = app.Environment.IsDevelopment(); // Nitro IDE dev-only
                // HotChocolate's built-in GET /graphql?sdl route serves the COMPLETE schema as SDL
                // text. DisableIntrospection does NOT cover it (that gates __schema/__type selections
                // inside a query document) and MapGraphQL carries no environment gate of its own, so
                // before this Production served the whole schema to an anonymous caller. Gated on the
                // same flag as introspection so the two disclosure routes stay in step.
                options.EnableSchemaRequests = exposeSchema;
            });
    }
}
