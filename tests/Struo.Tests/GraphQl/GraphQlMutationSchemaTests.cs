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
            .AddSingleton<IM2MDescriptorSource>(FakeMetadataFixtures.M2MSource())
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
    public async Task Create_input_includes_structured_multivalue_and_m2m_fields()
    {
        var sdl = await BuildSdlAsync();
        var block = InputBlock(sdl, "ArticleCreateInput");

        // plain scalars + M2O FK — the baseline fields every mutation input exposes
        block.Should().Contain("status: String");
        block.Should().Contain("publishedAt: DateTime");
        block.Should().Contain("categoryId: ID");

        // deferred kinds now typed
        block.Should().Contain("heroImageId: ID");          // Image scalar own-field
        block.Should().Contain("regions: [String!]");        // MultiSelect
        block.Should().Contain("keywords: [TagItemInput!]"); // Tags
        block.Should().Contain("attributes: Any");           // Json
        block.Should().Contain("gallery: [ID!]");            // Files
        block.Should().Contain("faqs: [ArticleFaqsItemInput!]"); // Repeater
        block.Should().Contain("tags: [ID!]");               // M2M relation

        // boundary: translatable own-field + create carries no version
        block.Should().NotContain("title");
        block.Should().NotContain("version:");
    }

    [Fact]
    public async Task Update_input_carries_deferred_kinds_and_version()
    {
        var sdl = await BuildSdlAsync();
        var block = InputBlock(sdl, "ArticleUpdateInput");
        block.Should().Contain("keywords: [TagItemInput!]");
        block.Should().Contain("faqs: [ArticleFaqsItemInput!]");
        block.Should().Contain("tags: [ID!]");
        block.Should().Contain("version: Long");
    }

    [Fact]
    public async Task TagItemInput_and_repeater_item_input_types_are_declared()
    {
        var sdl = await BuildSdlAsync();

        var tag = InputBlock(sdl, "TagItemInput");
        tag.Should().Contain("value: String!");
        tag.Should().Contain("label: String");

        var faq = InputBlock(sdl, "ArticleFaqsItemInput");
        faq.Should().Contain("question: String");   // Text sub-field, nullable in input
        faq.Should().Contain("answer: String");     // Textarea sub-field
    }

    [Fact]
    public async Task Update_input_matches_create_plus_version()
    {
        var sdl = await BuildSdlAsync();
        sdl.Should().Contain("updateArticle(");
        var block = InputBlock(sdl, "ArticleUpdateInput");
        block.Should().Contain("status: String");
        block.Should().Contain("categoryId: ID");
        block.Should().Contain("version: Long"); // optimistic-concurrency token, update-only
    }

    [Fact]
    public async Task Create_and_update_inputs_include_translations_list()
    {
        var sdl = await BuildSdlAsync();
        InputBlock(sdl, "ArticleCreateInput").Should().Contain("translations: [ArticleTranslationInput!]");
        InputBlock(sdl, "ArticleUpdateInput").Should().Contain("translations: [ArticleTranslationInput!]");
    }

    [Fact]
    public async Task Translation_wrapper_input_has_locale_and_typed_fields()
    {
        var sdl = await BuildSdlAsync();
        var wrapper = InputBlock(sdl, "ArticleTranslationInput");
        wrapper.Should().Contain("locale: String!");
        wrapper.Should().Contain("fields: ArticleTranslationFieldsInput!");
    }

    [Fact]
    public async Task Translation_fields_input_lists_translatable_fields_all_nullable()
    {
        var sdl = await BuildSdlAsync();
        var fields = InputBlock(sdl, "ArticleTranslationFieldsInput");
        fields.Should().Contain("title: String");          // Text (nullable in input; required validated server-side)
        fields.Should().Contain("body: String");           // RichText -> String
        fields.Should().Contain("seoOgImageId: ID");        // translatable Image -> ID
        fields.Should().NotContain("String!");              // no required marker on the field-map
    }

    [Fact]
    public async Task Translatable_fields_do_not_leak_to_top_level_inputs()
    {
        var sdl = await BuildSdlAsync();
        // body/seoOgImageId are translatable -> present ONLY inside the fields input, never as
        // top-level own-fields of the create/update input.
        InputBlock(sdl, "ArticleCreateInput").Should().NotContain("body:");
        InputBlock(sdl, "ArticleCreateInput").Should().NotContain("seoOgImageId:");
    }

    [Fact]
    public async Task Collection_without_sidecar_has_no_translations_input()
    {
        var sdl = await BuildSdlAsync();
        // Category has no translation sidecar -> no translations field, no translation input types.
        InputBlock(sdl, "CategoryCreateInput").Should().NotContain("translations");
        sdl.Should().NotContain("CategoryTranslationInput");
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
