// tests/Struo.Tests/GraphQl/GraphQlMutationSchemaTests.cs
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Serialization;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Xunit;

namespace Struo.Tests.GraphQl;

public class GraphQlMutationSchemaTests
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
    public async Task Mutation_has_create_field_per_collection()
    {
        var sdl = await BuildSdlAsync();
        sdl.Should().Contain("createArticle(");
        sdl.Should().Contain("createCategory(");
        sdl.Should().Contain("input ArticleCreateInput");
    }

    [Fact]
    public async Task Create_input_has_writable_scalars_and_m2o_fk_only()
    {
        var sdl = await BuildSdlAsync();
        var block = InputBlock(sdl, "ArticleCreateInput");

        block.Should().Contain("status: String");        // Select -> String
        block.Should().Contain("publishedAt: DateTime");  // DateTime
        block.Should().Contain("categoryId: ID");         // M2O FK

        // Deferred kinds must NOT appear in the 8b.1 create input:
        block.Should().NotContain("regions");   // MultiSelect
        block.Should().NotContain("keywords");  // Tags
        block.Should().NotContain("attributes");// Json
        block.Should().NotContain("gallery");   // Files
        block.Should().NotContain("faqs");      // Repeater
        block.Should().NotContain("heroImageId"); // Image
        // Title is translatable and not on the entity's FieldToProperty -> excluded here (8b.2).
        block.Should().NotContain("version");   // create carries no concurrency token
    }

    // Returns the SDL text of a single `input X { ... }` block.
    private static string InputBlock(string sdl, string typeName)
    {
        var start = sdl.IndexOf("input " + typeName, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"schema should declare input {typeName}");
        var open = sdl.IndexOf('{', start);
        var close = sdl.IndexOf('}', open);
        return sdl[open..close];
    }
}
