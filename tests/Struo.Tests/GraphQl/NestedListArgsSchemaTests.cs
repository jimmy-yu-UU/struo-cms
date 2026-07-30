// tests/Struo.Tests/GraphQl/NestedListArgsSchemaTests.cs
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Serialization;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Schema-shape gate: O2M/M2M relation fields declare
/// filter/sort/limit/offset args; M2O relation fields declare none.
/// Mirrors GraphQlSchemaTests' schema-build harness verbatim (no reusable
/// helper exists there, so the build code is inlined here rather than
/// invented anew).
/// </summary>
public class NestedListArgsSchemaTests
{
    private static async Task<string> BuildSdlAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource, FakeGraphQlDataSource>()
            .AddSingleton<StruoTypeModule>();

        var executor = await services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();

        return SchemaFormatter.FormatAsString(executor.Schema);
    }

    [Fact]
    public async Task ToMany_relation_fields_expose_list_args_and_m2o_does_not()
    {
        var sdl = await BuildSdlAsync();

        // Article.tags is M2M -> filter/sort/limit/offset args, filter type = TagFilterInput.
        sdl.Should().MatchRegex(@"tags\(filter:\s*TagFilterInput,\s*sort:\s*\[String!\],\s*limit:\s*Int,\s*offset:\s*Int\):\s*\[Tag!\]");

        // Category.articles is O2M -> filter/sort/limit/offset args, filter type = ArticleFilterInput.
        sdl.Should().MatchRegex(@"articles\(filter:\s*ArticleFilterInput,\s*sort:\s*\[String!\],\s*limit:\s*Int,\s*offset:\s*Int\):\s*\[Article!\]");

        // Article.category is M2O -> no argument list at all.
        sdl.Should().MatchRegex(@"category:\s*Category\r?\n");

        // Category.parent is M2O (self-referential) -> no argument list at all.
        sdl.Should().MatchRegex(@"parent:\s*Category\r?\n");
    }
}
