// tests/Struo.Tests/GraphQl/GraphQlSchemaTests.cs
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

    // Same setup as BuildSdlAsync, but ends in BuildSchemaAsync() to hand back the built
    // ISchemaDefinition directly (for tests that inspect specific input-object fields/types
    // rather than substring-matching printed SDL).
    private static async Task<ISchemaDefinition> BuildSchemaAsync()
    {
        var services = new ServiceCollection()
            .AddSingleton<IMetadataProvider>(FakeMetadataFixtures.Provider())
            .AddSingleton<IEntityRegistry>(FakeMetadataFixtures.Registry())
            .AddScoped<IGraphQlDataSource, FakeGraphQlDataSource>()
            .AddSingleton<StruoTypeModule>();

        return await services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddMutationType(d => d.Name("Mutation").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddType<LongType>().AddType<DateTimeType>().AddType<DateType>()
            .AddType<UuidType>().AddType<AnyType>().AddJsonTypeConverter()
            .AddTypeModule<StruoTypeModule>()
            .BuildSchemaAsync();
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
        // 8c.3b: M2M relation fields gain nested-list args (filter/sort/limit/offset).
        sdl.Should().Contain("tags(filter: TagFilterInput, sort: [String!], limit: Int, offset: Int): [Tag!]");
        sdl.Should().Contain("translations: [Translation!]");
    }

    [Fact]
    public async Task Hidden_and_password_fields_are_absent()
    {
        var sdl = await BuildSdlAsync();

        sdl.ToLowerInvariant().Should().NotContain("password");
    }

    [Fact]
    public async Task ArticleFilterInput_has_nested_category_relation_filter()
    {
        var schema = await BuildSchemaAsync();
        var input = schema.Types.OfType<HotChocolate.Types.IInputObjectTypeDefinition>()
            .Single(t => t.Name == "ArticleFilterInput");

        input.Fields.Any(f => f.Name == "category" && f.Type.NamedType().Name == "CategoryFilterInput")
            .Should().BeTrue();
        // FK operator field is retained (parity with REST allowlist).
        input.Fields.Any(f => f.Name == "categoryId" && f.Type.NamedType().Name == "IdFilter")
            .Should().BeTrue();
    }

    [Fact]
    public async Task CategoryFilterInput_is_self_referential_via_parent()
    {
        var schema = await BuildSchemaAsync();
        var input = schema.Types.OfType<HotChocolate.Types.IInputObjectTypeDefinition>()
            .Single(t => t.Name == "CategoryFilterInput");

        input.Fields.Any(f => f.Name == "parent" && f.Type.NamedType().Name == "CategoryFilterInput")
            .Should().BeTrue();
        input.Fields.Any(f => f.Name == "name" && f.Type.NamedType().Name == "StringFilter")
            .Should().BeTrue();
    }

    [Fact]
    public async Task ArticleFilterInput_has_nested_tags_m2m_relation_filter()
    {
        var schema = await BuildSchemaAsync();
        var input = schema.Types.OfType<HotChocolate.Types.IInputObjectTypeDefinition>()
            .Single(t => t.Name == "ArticleFilterInput");

        // M2M relation -> nested target FilterInput (ANY/EXISTS). M2M carries no FK column,
        // so there is only the nested filter field (no "tagsId").
        input.Fields.Any(f => f.Name == "tags" && f.Type.NamedType().Name == "TagFilterInput")
            .Should().BeTrue();
        // 8c.1 M2O fields are retained.
        input.Fields.Any(f => f.Name == "category" && f.Type.NamedType().Name == "CategoryFilterInput")
            .Should().BeTrue();
        input.Fields.Any(f => f.Name == "categoryId" && f.Type.NamedType().Name == "IdFilter")
            .Should().BeTrue();
    }

    [Fact]
    public async Task CategoryFilterInput_has_nested_articles_o2m_relation_filter()
    {
        var schema = await BuildSchemaAsync();
        var input = schema.Types.OfType<HotChocolate.Types.IInputObjectTypeDefinition>()
            .Single(t => t.Name == "CategoryFilterInput");

        // O2M relation -> nested target FilterInput. This also proves the Category <-> Article
        // cyclic input reference (CategoryFilterInput.articles -> ArticleFilterInput.category ->
        // CategoryFilterInput) resolves by name without a build loop (BuildSchemaAsync would throw).
        input.Fields.Any(f => f.Name == "articles" && f.Type.NamedType().Name == "ArticleFilterInput")
            .Should().BeTrue();
        // 8c.1 M2O self-reference is retained.
        input.Fields.Any(f => f.Name == "parent" && f.Type.NamedType().Name == "CategoryFilterInput")
            .Should().BeTrue();
    }
}
