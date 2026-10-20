// tests/Struo.Tests/GraphQl/GraphQlNameCollisionTests.cs
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Models;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// The startup name check: collisions between collections and the schema's fixed names fail with
/// one message naming the collection(s) and name(s), and the fixed-name list cannot drift from the
/// schema the module really builds.
/// </summary>
[Collection("ApiIntegration")]
public class GraphQlNameCollisionTests(ApiFactory factory)
{
    private static StruoTypeModule ModuleFor(IReadOnlyList<CollectionMetadata> collections) =>
        new(new SetProvider(collections), new BareRegistry(), new NoM2MSource());

    private static CollectionMetadata Bare(string name) =>
        new() { Name = name, Label = name, FieldGroups = [], Fields = [], Relations = [] };

    private static MetadataException Collide(params string[] names) =>
        FluentActions.Invoking(() => ModuleFor(names.Select(Bare).ToList()).ValidateNames())
            .Should().Throw<MetadataException>().Which;

    [Theory]
    [InlineData("revision", "Revision")]
    [InlineData("query", "Query")]
    [InlineData("string", "String")]
    [InlineData("tagItem", "TagItem")]
    public void A_collection_named_like_a_fixed_type_fails_naming_both(string collection, string typeName)
    {
        var ex = Collide(collection);

        ex.Message.Should().Contain($"'{collection}'").And.Contain($"'{typeName}'")
            .And.Contain("reserves");
    }

    [Fact]
    public void Two_collections_with_a_derived_type_clash_fail_naming_both()
    {
        // article's list wrapper type is ArticleList, which is also articleList's object type.
        var ex = Collide("article", "articleList");

        ex.Message.Should().Contain("'article'").And.Contain("'articleList'")
            .And.Contain("type name 'ArticleList'");
    }

    [Fact]
    public void Two_collections_with_a_root_field_clash_fail_naming_the_field()
    {
        // category's list field and categories' single field are both `categories`.
        var ex = Collide("category", "categories");

        ex.Message.Should().Contain("'category'").And.Contain("'categories'")
            .And.Contain("Query field name 'categories'");
    }

    [Fact]
    public void All_clashes_are_reported_in_one_message()
    {
        var ex = Collide("revision", "category", "categories");

        ex.Message.Should().Contain("'Revision'").And.Contain("'categories'");
    }

    [Fact]
    public void Generated_names_are_compared_exactly()
    {
        // `fooBar` and `foobar` as collection names are rejected by the scanner as case-insensitive
        // duplicates, so the validator is exercised here with surfaces directly: `FooBar` and
        // `Foobar` differ, `FooBar` and `FooBar` do not.
        FluentActions.Invoking(() => GraphQlNameValidator.Validate(
            [Surface("a", "FooBar"), Surface("b", "Foobar")])).Should().NotThrow();
        FluentActions.Invoking(() => GraphQlNameValidator.Validate(
            [Surface("a", "FooBar"), Surface("b", "FooBar")])).Should().Throw<MetadataException>();
    }

    [Fact]
    public void Three_colliding_collections_are_all_named()
    {
        var ex = FluentActions.Invoking(() => GraphQlNameValidator.Validate(
            [Surface("a", "Dup"), Surface("b", "Dup"), Surface("c", "Dup")]))
            .Should().Throw<MetadataException>().Which;

        ex.Message.Should().Contain("Collections 'a', 'b', 'c' all generate the type name 'Dup'");
    }

    private static CollectionSurface Surface(string collection, string typeName) =>
        new(collection, [typeName], [], []);

    [Fact]
    public void The_fixture_collections_pass()
    {
        var module = new StruoTypeModule(
            FakeMetadataFixtures.Provider(), FakeMetadataFixtures.Registry(), FakeMetadataFixtures.M2MSource());

        FluentActions.Invoking(module.ValidateNames).Should().NotThrow();
    }

    [Fact]
    public async Task The_host_schema_matches_the_fixed_name_lists_and_its_collections_pass()
    {
        var module = factory.Services.GetRequiredService<StruoTypeModule>();
        var surfaces = module.DescribeCollections();
        var executor = await factory.Services.GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync();
        var schema = executor.Schema;

        // The shipped Blog sample and framework collections generate no clashing names.
        FluentActions.Invoking(module.ValidateNames).Should().NotThrow();

        var collectionTypes = surfaces.SelectMany(s => s.TypeNames).ToHashSet(StringComparer.Ordinal);
        var sharedTypes = schema.Types.Select(t => t.Name)
            .Where(n => !n.StartsWith("__", StringComparison.Ordinal) && !collectionTypes.Contains(n))
            .ToHashSet(StringComparer.Ordinal);
        sharedTypes.Should().BeEquivalentTo(GraphQlNameValidator.FixedTypeNames);

        RootFieldsOutsideCollections(schema, "Query", surfaces.SelectMany(s => s.QueryFields))
            .Should().BeEquivalentTo(GraphQlNameValidator.FixedRootFields);
        RootFieldsOutsideCollections(schema, "Mutation", surfaces.SelectMany(s => s.MutationFields))
            .Should().BeEquivalentTo(GraphQlNameValidator.FixedRootFields);

        // Every collection-derived name really is in the schema.
        schema.Types.Select(t => t.Name).Should().Contain(collectionTypes);
    }

    [Fact]
    public async Task Schema_build_surfaces_the_collision_message()
    {
        var build = async () => await new ServiceCollection()
            .AddSingleton<IMetadataProvider>(new SetProvider([Bare("article"), Bare("articleList")]))
            .AddSingleton<IEntityRegistry>(new BareRegistry())
            .AddSingleton<IM2MDescriptorSource>(new NoM2MSource())
            .AddSingleton<StruoTypeModule>()
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("_service").Type<StringType>().Resolve(_ => "x"))
            .AddTypeModule<StruoTypeModule>()
            .BuildSchemaAsync();

        var ex = (await build.Should().ThrowAsync<Exception>()).Which;
        Flatten(ex).Should().Contain(e => e is MetadataException && e.Message.Contains("'ArticleList'"));
    }

    private static IEnumerable<Exception> Flatten(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException!)
            yield return e;
    }

    private static IEnumerable<string> RootFieldsOutsideCollections(
        ISchemaDefinition schema, string root, IEnumerable<string> collectionFields)
    {
        var owned = collectionFields.ToHashSet(StringComparer.Ordinal);
        return schema.Types.OfType<IObjectTypeDefinition>().Single(t => t.Name == root)
            .Fields.Select(f => f.Name)
            .Where(n => !n.StartsWith("__", StringComparison.Ordinal) && !owned.Contains(n));
    }

    private sealed class SetProvider(IReadOnlyList<CollectionMetadata> all) : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => all;

        public CollectionMetadata? GetCollection(string name) =>
            all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class BareRegistry : IEntityRegistry
    {
        public EntityDescriptor? Get(string collection) =>
            new(typeof(object), new Dictionary<string, string>(), "Id");
    }

    private sealed class NoM2MSource : IM2MDescriptorSource
    {
        public IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection) => [];
    }
}
