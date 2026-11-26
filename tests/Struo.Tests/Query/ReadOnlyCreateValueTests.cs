using Struo.Tests.Support.Seeding;
using System.Text.Json;
using System.Text.Json.Serialization;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// A create binds every non-system <c>ReadOnly</c> field to the value a new instance of the entity has,
/// whatever the body carries; a <c>BeforeWrite</c> interceptor that sets one afterwards wins; an update
/// never reaches one.
/// </summary>
public sealed class ReadOnlyCreateValueTests : IDisposable
{
    private static readonly Guid InitialGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [SugarTable("ro_create_item")]
    [CmsCollection("RoCreateItem", Name = "roCreateItem")]
    public sealed class Item : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "NullableText", Interface = FieldInterface.Text, ReadOnly = true)]
        public string? NullableText { get; set; } = "x";

        [CmsField(Label = "Text", Interface = FieldInterface.Text, ReadOnly = true)]
        public string Text { get; set; } = "x";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "NullableNumber", Interface = FieldInterface.Number, ReadOnly = true)]
        public int? NullableNumber { get; set; } = 5;

        [CmsField(Label = "Number", Interface = FieldInterface.Number, ReadOnly = true)]
        public int Number { get; set; } = 5;

        [CmsField(Label = "Flag", Interface = FieldInterface.Boolean, ReadOnly = true)]
        public bool Flag { get; set; } = true;

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Key", Interface = FieldInterface.Hidden, ReadOnly = true)]
        public Guid? Key { get; set; } = Guid.Parse("11111111-1111-1111-1111-111111111111");

        [CmsField(Label = "Items", Interface = FieldInterface.Hidden, ReadOnly = true)]
        public List<string> Items { get; set; } = ["a"];

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Aliased", Interface = FieldInterface.Text, ReadOnly = true)]
        [JsonPropertyName("alias")]
        public string? Aliased { get; set; } = "x";
    }

    private sealed class SetsReadOnlyFields : IItemWriteInterceptor
    {
        public bool AppliesTo(string collection) => string.Equals(collection, "roCreateItem", StringComparison.OrdinalIgnoreCase);

        public Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
        {
            var item = (Item)context.Entity;
            item.NullableText = "interceptor";
            item.Text = "interceptor";
            item.Items = ["interceptor"];
            return Task.CompletedTask;
        }
    }

    private const string HostileBody = """
        {"name":"n","nullableText":"c","NULLABLETEXT":"c","text":"c","tExT":"c","nullableNumber":9,"number":9,
         "flag":false,"key":"22222222-2222-2222-2222-222222222222","items":["z"],"alias":"c","aliased":"c","ALIAS":"c"}
        """;

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly SqlSugarItemRepository _repo;
    private readonly ItemDeserializer _deserializer;
    private readonly Struo.Domain.Metadata.Models.CollectionMetadata _meta;
    private readonly Func<IEnumerable<IItemWriteInterceptor>?, ItemService> _service;

    public ReadOnlyCreateValueTests()
    {
        _db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<Item>();
        _db.CodeFirst.InitTables<Role>();
        _db.CodeFirst.InitTables<Language>();
        _db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(_db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[] { typeof(Item), typeof(Role) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["roCreateItem"] = typeof(Item),
            ["role"] = typeof(Role),
        });
        _repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        _deserializer = new ItemDeserializer(registry, graph, new RichTextCleaner(new GanssHtmlSanitizer()));
        _meta = provider.GetCollection("roCreateItem")!;
        var expander = new RelationExpander(_repo, graph, new StruoQueryOptions());
        var user = new TestCurrentUserAccessor(Guid.Empty);
        var revisions = new SqlSugarRevisionStore(_db, user);
        var snapshots = new RevisionSnapshotBuilder(_repo, provider, registry, graph);
        _service = interceptors => TestItemService.Create(
            _repo, provider, registry, graph, expander, graph, new LanguageProvider(_db), user, revisions, snapshots,
            interceptors: interceptors);
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    private Item Bind(string json) => (Item)_deserializer.DeserializeForCreate("roCreateItem", Body(json), _meta);

    private async Task<Item> StoredAsync(IReadOnlyDictionary<string, object?> created) =>
        (Item)(await _repo.GetByIdAsync("roCreateItem", created["id"]!.ToString()!))!;

    public static TheoryData<string, Func<Item, object?>, object?> InitialValues() => new()
    {
        { nameof(Item.NullableText), i => i.NullableText, "x" },
        { nameof(Item.Text), i => i.Text, "x" },
        { nameof(Item.NullableNumber), i => i.NullableNumber, 5 },
        { nameof(Item.Number), i => i.Number, 5 },
        { nameof(Item.Flag), i => i.Flag, true },
        { nameof(Item.Key), i => i.Key, InitialGuid },
        { nameof(Item.Aliased), i => i.Aliased, "x" },
    };

    [Theory]
    [MemberData(nameof(InitialValues))]
    public void A_create_binds_a_read_only_field_to_its_initial_value(
        string field, Func<Item, object?> read, object? expected)
    {
        Bind("""{"name":"n"}""").Should().Match<Item>(i => Equals(read(i), expected), field);
    }

    [Theory]
    [MemberData(nameof(InitialValues))]
    public void A_body_that_names_a_read_only_field_in_any_casing_does_not_change_it(
        string field, Func<Item, object?> read, object? expected)
    {
        Bind(HostileBody).Should().Match<Item>(i => Equals(read(i), expected), field);
    }

    [Fact]
    public void A_create_binds_a_list_field_to_its_initial_items()
    {
        Bind(HostileBody).Items.Should().Equal("a");
    }

    [Fact]
    public void Each_create_gets_its_own_instance_of_a_list_field()
    {
        var first = Bind("""{"name":"n"}""");
        var second = Bind("""{"name":"n"}""");

        first.Items.Should().NotBeSameAs(second.Items);
    }

    [Fact]
    public async Task A_create_stores_the_initial_values_of_its_read_only_fields()
    {
        var created = await _service(null).CreateAsync("roCreateItem", Body(HostileBody));

        var stored = await StoredAsync(created);
        stored.NullableText.Should().Be("x");
        stored.Text.Should().Be("x");
        stored.NullableNumber.Should().Be(5);
        stored.Number.Should().Be(5);
        stored.Flag.Should().BeTrue();
        stored.Key.Should().Be(InitialGuid);
        stored.Items.Should().Equal("a");
        stored.Aliased.Should().Be("x");
    }

    [Fact]
    public async Task A_before_write_interceptor_that_sets_a_read_only_field_wins()
    {
        var created = await _service([new SetsReadOnlyFields()]).CreateAsync("roCreateItem", Body(HostileBody));

        var stored = await StoredAsync(created);
        stored.NullableText.Should().Be("interceptor");
        stored.Text.Should().Be("interceptor");
        stored.Items.Should().Equal("interceptor");
        stored.Number.Should().Be(5);
    }

    [Fact]
    public async Task An_update_never_reaches_a_read_only_field()
    {
        var svc = _service(null);
        var created = await svc.CreateAsync("roCreateItem", Body("""{"name":"n"}"""));
        var id = created["id"]!.ToString()!;

        await svc.UpdateAsync("roCreateItem", id, Body(HostileBody));

        var stored = await StoredAsync(created);
        stored.Name.Should().Be("n");
        stored.NullableText.Should().Be("x");
        stored.Text.Should().Be("x");
        stored.NullableNumber.Should().Be(5);
        stored.Number.Should().Be(5);
        stored.Flag.Should().BeTrue();
        stored.Key.Should().Be(InitialGuid);
        stored.Items.Should().Equal("a");
        stored.Aliased.Should().Be("x");
    }

    [Fact]
    public async Task A_role_created_through_the_items_service_is_not_a_super_admin()
    {
        var created = await _service(null).CreateAsync("role", Body("""{"name":"editors","isSuperAdmin":true}"""));

        var stored = (Role)(await _repo.GetByIdAsync("role", created["id"]!.ToString()!))!;
        stored.IsSuperAdmin.Should().BeFalse();
    }
}
