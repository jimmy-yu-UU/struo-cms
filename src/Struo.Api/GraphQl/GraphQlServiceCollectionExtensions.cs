// src/Struo.Api/GraphQl/GraphQlServiceCollectionExtensions.cs
using HotChocolate.AspNetCore;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Struo.Api.GraphQl;

public static class GraphQlServiceCollectionExtensions
{
    public static IServiceCollection AddStruoGraphQl(this IServiceCollection services, IHostEnvironment env)
    {
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
            .DisableIntrospection(!env.IsDevelopment())
            .ModifyOptions(o => o.DefaultQueryDependencyInjectionScope = DependencyInjectionScope.Request);

        return services;
    }

    public static void MapStruoGraphQl(this WebApplication app)
        => app.MapGraphQL("/graphql")
              .WithOptions(options =>
                  options.Tool.Enable = app.Environment.IsDevelopment()); // Nitro IDE dev-only
}
