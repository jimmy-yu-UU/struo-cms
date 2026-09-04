using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query.Write;
using Struo.Domain.Auditing;
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
        // Renamed column: pins that a payload write reaches the aliased column, not the CLR name.
        [SugarColumn(IsNullable = true, ColumnName = "note_text")] public string? Alias { get; set; }
        public int Sort { get; set; }
    }

    // AuditableEntity junction fixture: pins (1) AuditAop's UpdatedAt/UpdatedBy stamping survives a
    // payload-changing resync, and (2) a nullable (int?) sort property converts without throwing.
    [SugarTable("mms_audit_links")]
    public sealed class MmsAuditLink : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
        public Guid ParentId { get; set; }
        public Guid ChildId { get; set; }
        [SugarColumn(IsNullable = true)] public string? Note { get; set; }
        [SugarColumn(IsNullable = true)] public int? Sort { get; set; }
    }

    private readonly SqliteTestDatabase _db = new();
    private readonly ISqlSugarClient _client;
    private readonly ListLogger<SqlSugarItemRepository> _log = new();
    private readonly SqlSugarItemRepository _repo;
    private static readonly Guid P = Guid.NewGuid();
    private static readonly Guid C1 = Guid.NewGuid(), C2 = Guid.NewGuid(), C3 = Guid.NewGuid();
    private static readonly Guid Actor = Guid.Empty;

    public ManyToManySyncTests()
    {
        _client = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _db.ConnectionString },
            new TestCurrentUserAccessor(Actor));
        _client.CodeFirst.InitTables<MmsLink>();
        _client.CodeFirst.InitTables<MmsAuditLink>();
        // The repository needs registry/graph/provider only for other members; pass empty ones.
        var collections = MetadataScanner.ScanTypes([]);
        _repo = new SqlSugarItemRepository(_client, new EntityRegistry(MetadataScanner.ScanDescriptors([])),
            new RelationshipGraph(collections, new Dictionary<string, Type>()), new CachedMetadataProvider(collections),
            new StruoQueryOptions(), _log);
    }

    private Task Sync(params JunctionLink[] links) =>
        _repo.SyncManyToManyAsync(typeof(MmsLink), nameof(MmsLink.ParentId), nameof(MmsLink.ChildId), nameof(MmsLink.Sort), P, links);

    private Task SyncAudit(params JunctionLink[] links) =>
        _repo.SyncManyToManyAsync(typeof(MmsAuditLink), nameof(MmsAuditLink.ParentId), nameof(MmsAuditLink.ChildId), nameof(MmsAuditLink.Sort), P, links);

    private List<MmsLink> Rows()
    {
        var parent = P;
        return _client.Queryable<MmsLink>().Where(l => l.ParentId == parent).OrderBy(l => l.Sort).ToList();
    }

    private List<MmsAuditLink> AuditRows()
    {
        var parent = P;
        return _client.Queryable<MmsAuditLink>().Where(l => l.ParentId == parent).OrderBy(l => l.Sort).ToList();
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
    public async Task Reordering_existing_targets_updates_sort_for_all_of_them_in_one_batch()
    {
        await Sync(JunctionLink.Bare(C1), JunctionLink.Bare(C2), JunctionLink.Bare(C3));
        var idsByChild = Rows().ToDictionary(r => r.ChildId, r => r.Id);

        await Sync(JunctionLink.Bare(C3), JunctionLink.Bare(C2), JunctionLink.Bare(C1));
        var rows = Rows();

        rows.Select(r => r.ChildId).Should().Equal(C3, C2, C1);
        rows.Select(r => r.Sort).Should().Equal(0, 1, 2);
        rows.Select(r => r.Id).Should().BeEquivalentTo(idsByChild.Values, "reordering only updates sort, never identity");
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
    public async Task Payload_property_with_a_renamed_column_round_trips()
    {
        await Sync(new JunctionLink(C1, new Dictionary<string, object?> { ["Alias"] = "first" }));
        await Sync(new JunctionLink(C1, new Dictionary<string, object?> { ["Alias"] = "second" }));
        Rows().Single().Alias.Should().Be("second", "a full-row update writes every column through its real DB mapping, alias included");
    }

    [Fact]
    public async Task Duplicate_rows_for_one_pair_are_repaired_keeping_the_lowest_pk_and_logging_one_warning()
    {
        // Insert the higher PK first and the lower PK second: if the repair ever regressed to
        // "keep whatever row the query happened to return first" instead of "keep the lowest PK",
        // insertion order alone would not catch it — this ordering would.
        var higher = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var lower = Guid.Parse("00000000-0000-0000-0000-000000000001");
        _client.Insertable(new MmsLink { Id = higher, ParentId = P, ChildId = C1, Note = "second", Sort = 1 }).ExecuteCommand();
        _client.Insertable(new MmsLink { Id = lower, ParentId = P, ChildId = C1, Note = "first", Sort = 0 }).ExecuteCommand();

        await Sync(JunctionLink.Bare(C1));

        var rows = Rows();
        var survivor = rows.Should().ContainSingle().Which;
        survivor.Id.Should().Be(lower, "the lowest-PK row survives");
        survivor.Note.Should().Be("first");
        _log.Entries.Should().ContainSingle(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("mms_links") && e.Message.Contains("duplicate"),
            "exactly one warning is logged for the whole repair, not one per duplicate row");
    }

    [Fact]
    public async Task Duplicate_target_id_within_links_throws_instead_of_silently_taking_the_last_one()
    {
        var act = async () => await Sync(
            new JunctionLink(C1, new Dictionary<string, object?> { ["Note"] = "a" }),
            new JunctionLink(C1, new Dictionary<string, object?> { ["Note"] = "b" }));

        var thrown = await act.Should().ThrowAsync<ArgumentException>();
        thrown.Which.Message.Should().Contain(C1.ToString());
    }

    [Fact]
    public async Task Empty_link_list_deletes_all_rows()
    {
        await Sync(JunctionLink.Bare(C1));
        await Sync();
        Rows().Should().BeEmpty();
    }

    [Fact]
    public async Task Payload_changing_resync_stamps_UpdatedAt_and_UpdatedBy_via_the_batched_full_row_update()
    {
        await SyncAudit(new JunctionLink(C1, new Dictionary<string, object?> { ["Note"] = "n" }));
        var before = AuditRows().Single();

        await Task.Delay(5); // guarantee a measurable clock tick between the two writes
        await SyncAudit(new JunctionLink(C1, new Dictionary<string, object?> { ["Note"] = "n2" }));
        var after = AuditRows().Single();

        after.UpdatedAt.Should().BeAfter(before.UpdatedAt, "the batched Updateable(...) call must still trigger AuditAop's UpdatedAt stamp");
        after.UpdatedBy.Should().Be(Actor);
        // A full-row update round-trips CreatedAt through SQLite's datetime storage too, which
        // truncates sub-millisecond precision — the value is logically untouched (AuditAop's
        // DataExecuting only overwrites Updated*, never Created*), so compare with a millisecond
        // tolerance rather than exact equality.
        after.CreatedAt.Should().BeCloseTo(before.CreatedAt, TimeSpan.FromMilliseconds(2),
            "a payload update must not disturb the original CreatedAt stamp");
    }

    [Fact]
    public async Task Nullable_int_sort_property_converts_without_throwing()
    {
        await SyncAudit(JunctionLink.Bare(C1), JunctionLink.Bare(C2));
        var rows = AuditRows();
        rows.Select(r => r.ChildId).Should().Equal(C1, C2);
        rows.Select(r => r.Sort).Should().Equal(0, 1);
    }

    public void Dispose() => _db.Dispose();
}
