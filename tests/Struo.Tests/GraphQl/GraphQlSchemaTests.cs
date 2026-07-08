// tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Serialization;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Builds the full dynamic schema (StruoTypeModule) against the Task-6 fake metadata fixtures (no DB)
/// and asserts on the printed SDL — the schema-shape gate for Task 7 (object/list/filter types + root
/// query fields per collection, incl. relations/files/repeater/translations, hidden/password excluded).
/// </summary>
public class GraphQlSchemaTests
{
    private static async Task<string> BuildSdlAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource, FakeGraphQlDataSource>()
            // AddTypeModule<T>() resolves T via GetRequiredService<T>() against application
            // services (not schema-scoped activation) — must be registered explicitly.
            .AddSingleton<StruoTypeModule>();

        var executor = await services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildRequestExecutorAsync();

        // v16: ISchema/Print() are gone — the schema handed back by the executor is an
        // ISchemaDefinition, and SchemaFormatter.FormatAsString renders its SDL.
        return SchemaFormatter.FormatAsString(executor.Schema);
    }

    [Fact]
    public async Task Query_has_single_and_list_fields_per_collection()
    {
        var sdl = await BuildSdlAsync();

        sdl.Should().Contain("article(");
        sdl.Should().Contain("articles(");
        sdl.Should().Contain("type Article");
        sdl.Should().Contain("type ArticleList");
        sdl.Should().Contain("input ArticleFilterInput");
    }

    [Fact]
    public async Task Article_type_maps_fields_relations_and_files()
    {
        var sdl = await BuildSdlAsync();

        sdl.Should().Contain("status: String");
        sdl.Should().Contain("regions: [String!]");
        sdl.Should().Contain("attributes: Any");
        sdl.Should().Contain("keywords: [TagItem!]");
        sdl.Should().Contain("faqs: [ArticleFaqsItem!]");
        sdl.Should().Contain("heroImageId: ID");
        sdl.Should().Contain("heroImage: File");
        sdl.Should().Contain("gallery: [ID!]");
        sdl.Should().Contain("galleryFiles: [File!]");
        sdl.Should().Contain("category: Category");
        sdl.Should().Contain("tags: [Tag!]");
        sdl.Should().Contain("translations: [Translation!]");
    }

    [Fact]
    public async Task Hidden_and_password_fields_are_absent()
    {
        var sdl = await BuildSdlAsync();

        sdl.ToLowerInvariant().Should().NotContain("password");
    }
}
