using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query.Write;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public sealed class ManyToManySyncTests : IDisposable
{
    [SugarTable("mms_links")]
    public sealed class MmsLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid ParentId { get; set; }
        public Guid ChildId { get; set; }
        [SugarColumn(IsNullable = true)] public string? Note { get; set; }
        [SugarColumn(IsNullable = true)] public int? Weight { get; set; }
        public int Sort { get; set; }
    }

    private readonly SqliteTestDatabase _db = new();
    private readonly ISqlSugarClient _client;
    private readonly ListLogger<SqlSugarItemRepository> _log = new();
    private readonly SqlSugarItemRepository _repo;
    private static readonly Guid P = Guid.NewGuid();
    private static readonly Guid C1 = Guid.NewGuid(), C2 = Guid.NewGuid(), C3 = Guid.NewGuid();

    public ManyToManySyncTests()
    {
        _client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _db.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        _client.CodeFirst.InitTables<MmsLink>();
        // The repository needs registry/graph/provider only for other members; pass empty ones.
        var collections = MetadataScanner.ScanTypes([]);
        _repo = new SqlSugarItemRepository(_client, new EntityRegistry(MetadataScanner.ScanDescriptors([])),
            new RelationshipGraph(collections, new Dictionary<string, Type>()), new CachedMetadataProvider(collections),
            new StruoQueryOptions(), _log);
    }

    private Task Sync(params JunctionLink[] links) =>
        _repo.SyncManyToManyAsync(typeof(MmsLink), nameof(MmsLink.ParentId), nameof(MmsLink.ChildId), nameof(MmsLink.Sort), P, links);

    private List<MmsLink> Rows()
    {
        var parent = P;
        return _client.Queryable<MmsLink>().Where(l => l.ParentId == parent).OrderBy(l => l.Sort).ToList();
    }

    [Fact]
    public async Task Bare_ids_insert_rows_with_sort_and_no_payload()
    {
        await Sync(JunctionLink.Bare(C1), JunctionLink.Bare(C2));
        var rows = Rows();
        rows.Select(r => r.ChildId).Should().Equal(C1, C2);
        rows.Select(r => r.Sort).Should().Equal(0, 1);
        rows.Should().AllSatisfy(r => r.Note.Should().BeNull());
    }

    [Fact]
    public async Task Resync_with_the_same_bare_ids_keeps_primary_keys_and_payload()
    {
        await Sync(new JunctionLink(C1, new Dictionary<string, object?> { ["Note"] = "keep-me" }), JunctionLink.Bare(C2));
        var before = Rows();

        await Sync(JunctionLink.Bare(C1), JunctionLink.Bare(C2));
        var after = Rows();

        after.Select(r => r.Id).Should().Equal(before.Select(r => r.Id), "diff-and-patch must not recreate rows");
        after.Single(r => r.ChildId == C1).Note.Should().Be("keep-me", "a bare id leaves payload untouched");
    }

    [Fact]
    public async Task Removed_targets_are_deleted_and_new_ones_inserted_without_touching_survivors()
    {
        await Sync(JunctionLink.Bare(C1), JunctionLink.Bare(C2));
        var c2Id = Rows().Single(r => r.ChildId == C2).Id;

        await Sync(JunctionLink.Bare(C2), JunctionLink.Bare(C3));
        var rows = Rows();
        rows.Select(r => r.ChildId).Should().Equal(C2, C3);
        rows.Single(r => r.ChildId == C2).Id.Should().Be(c2Id);
        rows.Single(r => r.ChildId == C2).Sort.Should().Be(0, "sort follows the new array order");
    }

    [Fact]
    public async Task Object_links_merge_only_the_given_payload_fields()
    {
        await Sync(new JunctionLink(C1, new Dictionary<string, object?> { ["Note"] = "n", ["Weight"] = 1 }));
        await Sync(new JunctionLink(C1, new Dictionary<string, object?> { ["Weight"] = 5 }));
        var row = Rows().Single();
        row.Note.Should().Be("n");
        row.Weight.Should().Be(5);
    }

    [Fact]
    public async Task Payload_can_be_set_to_null_explicitly()
    {
        await Sync(new JunctionLink(C1, new Dictionary<string, object?> { ["Note"] = "n" }));
        await Sync(new JunctionLink(C1, new Dictionary<string, object?> { ["Note"] = null }));
        Rows().Single().Note.Should().BeNull();
    }

    [Fact]
    public async Task Duplicate_rows_for_one_pair_are_repaired_keeping_the_first_and_logging_a_warning()
    {
        _client.Insertable(new MmsLink { Id = Guid.NewGuid(), ParentId = P, ChildId = C1, Note = "first", Sort = 0 }).ExecuteCommand();
        _client.Insertable(new MmsLink { Id = Guid.NewGuid(), ParentId = P, ChildId = C1, Note = "second", Sort = 1 }).ExecuteCommand();

        await Sync(JunctionLink.Bare(C1));

        var rows = Rows();
        rows.Should().ContainSingle().Which.Note.Should().Be("first");
        _log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("mms_links") && e.Message.Contains("duplicate"));
    }

    [Fact]
    public async Task Empty_link_list_deletes_all_rows()
    {
        await Sync(JunctionLink.Bare(C1));
        await Sync();
        Rows().Should().BeEmpty();
    }

    public void Dispose() => _db.Dispose();
}
