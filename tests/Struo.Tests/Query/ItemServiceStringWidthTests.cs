using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Metadata;
using Struo.Tests.Support;
using Struo.Tests.Support.Seeding;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// A string field with no declared MaxLength is limited to its column width on write, so an
/// over-long value is a validation error on every backend instead of a database length error.
/// </summary>
public sealed class ItemServiceStringWidthTests : IDisposable
{
    private const string Collection = "widthProbe";
    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ItemService _svc;

    public ItemServiceStringWidthTests()
    {
        var db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<WidthProbeEntity>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        Type[] types = [typeof(WidthProbeEntity)];
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var graph = new RelationshipGraph(collections, MetadataScanner.CollectionTypeMap(types));
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var user = new TestCurrentUserAccessor(Guid.Empty);
        _svc = TestItemService.Create(
            repo, provider, registry, graph, expander, graph, new LanguageProvider(db), user,
            new SqlSugarRevisionStore(db, user), new RevisionSnapshotBuilder(repo, provider, registry, graph));
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(string field, int length)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(
            new Dictionary<string, string> { [field] = new string('x', length) }));
        return doc.RootElement.Clone();
    }

    [Theory]
    [InlineData("plainText", 255)]
    [InlineData("hiddenText", 255)]
    [InlineData("clockText", 255)]
    [InlineData("uuidText", 255)]
    [InlineData("narrowText", 100)]
    [InlineData("explicitLimit", 120)]
    [InlineData("typedLengthText", 64)]
    public async Task A_value_at_the_limit_is_stored(string field, int limit)
    {
        var created = await _svc.CreateAsync(Collection, Body(field, limit));

        created.Should().NotBeNull();
    }

    [Theory]
    [InlineData("plainText", 255)]
    [InlineData("hiddenText", 255)]
    [InlineData("clockText", 255)]
    [InlineData("uuidText", 255)]
    [InlineData("narrowText", 100)]
    [InlineData("explicitLimit", 120)]
    [InlineData("typedLengthText", 64)]
    public async Task A_value_over_the_limit_is_a_validation_error(string field, int limit)
    {
        var act = () => _svc.CreateAsync(Collection, Body(field, limit + 1));

        await act.Should().ThrowAsync<QueryException>()
            .WithMessage($"Field '{field}' exceeds maximum length {limit}.");
    }

    [Theory]
    [InlineData("bodyText")]
    [InlineData("richBody")]
    [InlineData("shapedText")]
    [InlineData("typedText")]
    public async Task A_field_without_a_column_width_takes_a_long_value(string field)
    {
        var created = await _svc.CreateAsync(Collection, Body(field, 4000));

        created.Should().NotBeNull();
    }
}
