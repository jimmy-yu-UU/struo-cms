using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Persistence;

public sealed class TranslationSidecarIndexPolicyTests
{
    [Fact]
    public void FromMetadata_derives_the_shipped_file_sidecar_key_and_group_name()
    {
        var collections = MetadataScanner.ScanTypes([typeof(File), typeof(MediaFolder)]);
        var policy = TranslationSidecarIndexPolicy.FromMetadata(collections);

        policy.UniqueGroupFor(typeof(FileTranslation), nameof(FileTranslation.FileId))
            .Should().Be("ux_file_translations_fk_locale");
        policy.UniqueGroupFor(typeof(FileTranslation), nameof(FileTranslation.Locale))
            .Should().Be("ux_file_translations_fk_locale");
    }

    [Fact]
    public void UniqueGroupFor_returns_null_for_non_key_properties_and_non_sidecar_types()
    {
        var collections = MetadataScanner.ScanTypes([typeof(File), typeof(MediaFolder)]);
        var policy = TranslationSidecarIndexPolicy.FromMetadata(collections);

        policy.UniqueGroupFor(typeof(FileTranslation), nameof(FileTranslation.Title)).Should().BeNull();
        policy.UniqueGroupFor(typeof(File), nameof(File.Id)).Should().BeNull();
    }

    [Fact]
    public void None_matches_nothing()
    {
        TranslationSidecarIndexPolicy.None
            .UniqueGroupFor(typeof(FileTranslation), nameof(FileTranslation.FileId))
            .Should().BeNull();
    }

    [Fact]
    public void Group_name_falls_back_to_the_lowercased_type_name_without_SugarTable()
    {
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(UnnamedSidecar)] = TranslationSidecarIndexPolicy.KeyFor(typeof(UnnamedSidecar), "ParentId", "Locale"),
        });

        policy.UniqueGroupFor(typeof(UnnamedSidecar), "ParentId").Should().Be("ux_unnamedsidecar_fk_locale");
    }

    private sealed class UnnamedSidecar
    {
        public long Id { get; set; }
        public Guid ParentId { get; set; }
        public string Locale { get; set; } = "";
    }

    // ── Hook end-to-end on SQLite. SchemaGuard is the production detector (uniqueness + column
    //    coverage, name-agnostic), so passing it is the acceptance criterion, not a proxy.

    [SugarTable("policy_probe_translations")]
    private sealed class PolicyProbeTranslation
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
        public Guid PolicyProbeId { get; set; }
        public string Locale { get; set; } = "";
        [SugarColumn(IsNullable = true)] public string? Title { get; set; }
    }

    private static TranslationSidecarIndexPolicy ProbePolicy() => new(new Dictionary<Type, TranslationSidecarKey>
    {
        [typeof(PolicyProbeTranslation)] = TranslationSidecarIndexPolicy.KeyFor(
            typeof(PolicyProbeTranslation), nameof(PolicyProbeTranslation.PolicyProbeId), nameof(PolicyProbeTranslation.Locale)),
    });

    private static ISqlSugarClient NewSqliteClient(SqliteTestDatabase db, TranslationSidecarIndexPolicy? policy) =>
        policy is null
            ? SqlSugarClientFactory.Create(
                new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
                new TestCurrentUserAccessor(Guid.Empty))
            : SqlSugarClientFactory.Create(
                new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = db.ConnectionString },
                new TestCurrentUserAccessor(Guid.Empty), policy);

    private static TranslationSidecarDescriptor ProbeDescriptor(ISqlSugarClient client) => new(
        client.EntityMaintenance.GetTableName(typeof(PolicyProbeTranslation)),
        client.EntityMaintenance.GetDbColumnName(nameof(PolicyProbeTranslation.PolicyProbeId), typeof(PolicyProbeTranslation)),
        client.EntityMaintenance.GetDbColumnName(nameof(PolicyProbeTranslation.Locale), typeof(PolicyProbeTranslation)));

    [Fact]
    public async Task With_policy_InitTables_emits_the_composite_unique_that_SchemaGuard_demands()
    {
        using var db = new SqliteTestDatabase();
        var client = NewSqliteClient(db, ProbePolicy());
        client.CodeFirst.InitTables(typeof(Struo.Infrastructure.Revisions.Revision), typeof(PolicyProbeTranslation));

        var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, [ProbeDescriptor(client)], default);
        await act.Should().NotThrowAsync();

        var pid = Guid.NewGuid();
        client.Insertable(new PolicyProbeTranslation { PolicyProbeId = pid, Locale = "en", Title = "a" }).ExecuteCommand();
        var dup = () => client.Insertable(new PolicyProbeTranslation { PolicyProbeId = pid, Locale = "en", Title = "b" }).ExecuteCommand();
        dup.Should().Throw<Exception>().Which.Message.Should().Contain("UNIQUE");
    }

    [Fact]
    public async Task Without_policy_the_same_sidecar_gets_no_unique_index()
    {
        using var db = new SqliteTestDatabase();
        var client = NewSqliteClient(db, policy: null);
        client.CodeFirst.InitTables(typeof(Struo.Infrastructure.Revisions.Revision), typeof(PolicyProbeTranslation));

        var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, [ProbeDescriptor(client)], default);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("policy_probe_translations");
    }

    [SugarTable("policy_probe_legacy_translations")]
    private sealed class LegacyAttributedTranslation
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
        [SugarColumn(UniqueGroupNameList = ["ux_policy_probe_legacy_translations_fk_locale"])]
        public Guid LegacyProbeId { get; set; }
        [SugarColumn(UniqueGroupNameList = ["ux_policy_probe_legacy_translations_fk_locale"])]
        public string Locale { get; set; } = "";
    }

    [Fact]
    public void Hand_written_group_with_the_same_name_dedupes_to_a_single_unique_index()
    {
        using var db = new SqliteTestDatabase();
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(LegacyAttributedTranslation)] = TranslationSidecarIndexPolicy.KeyFor(
                typeof(LegacyAttributedTranslation), nameof(LegacyAttributedTranslation.LegacyProbeId), nameof(LegacyAttributedTranslation.Locale)),
        });
        var client = NewSqliteClient(db, policy);
        client.CodeFirst.InitTables(typeof(LegacyAttributedTranslation));

        var uniqueIndexCount = client.Ado.GetInt(
            "SELECT count(*) FROM sqlite_master WHERE type='index' AND tbl_name='policy_probe_legacy_translations' AND sql LIKE 'CREATE UNIQUE INDEX%'");
        uniqueIndexCount.Should().Be(1);
    }

    [SugarTable("policy_probe_foreign_translations")]
    private sealed class ForkOwnedAttributedTranslation
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
        [SugarColumn(UniqueGroupNameList = ["uq_fork_owned_key"])]
        public Guid ForkProbeId { get; set; }
        [SugarColumn(UniqueGroupNameList = ["uq_fork_owned_key"])]
        public string Locale { get; set; } = "";
        // A third member of the fork's own group, deliberately NOT covered by the policy. If the hook
        // unions, ForkProbeId/Locale end up in BOTH groups (2-column derived group + 3-column
        // fork-owned group) -> 2 distinct composite indexes, both spanning ForkProbeId+Locale. If the
        // hook overwrote instead, ForkProbeId/Locale would be stripped out of "uq_fork_owned_key",
        // leaving it with only Variant (a single-column group that can never cover the two key columns)
        // -> the assertion below fails either on count or on column coverage.
        [SugarColumn(UniqueGroupNameList = ["uq_fork_owned_key"])]
        public string Variant { get; set; } = "";
    }

    // Discriminates the union guard from mere name-matching: the attribute's group name
    // ("uq_fork_owned_key") deliberately differs from what TranslationSidecarIndexPolicy.KeyFor
    // derives for this table ("ux_policy_probe_foreign_translations_fk_locale"), so a hook that
    // OVERWRITES column.UIndexGroupNameList instead of unioning into it would silently drop the
    // fork-owned group, leaving only 1 unique index instead of 2.
    [Fact]
    public void Hand_written_group_with_a_different_name_is_kept_alongside_the_derived_one()
    {
        using var db = new SqliteTestDatabase();
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(ForkOwnedAttributedTranslation)] = TranslationSidecarIndexPolicy.KeyFor(
                typeof(ForkOwnedAttributedTranslation), nameof(ForkOwnedAttributedTranslation.ForkProbeId), nameof(ForkOwnedAttributedTranslation.Locale)),
        });
        var client = NewSqliteClient(db, policy);
        client.CodeFirst.InitTables(typeof(ForkOwnedAttributedTranslation));

        var fkColumn = client.EntityMaintenance.GetDbColumnName(nameof(ForkOwnedAttributedTranslation.ForkProbeId), typeof(ForkOwnedAttributedTranslation));
        var localeColumn = client.EntityMaintenance.GetDbColumnName(nameof(ForkOwnedAttributedTranslation.Locale), typeof(ForkOwnedAttributedTranslation));

        var indexSqlStatements = client.Ado.SqlQuery<string>(
            "SELECT sql FROM sqlite_master WHERE type='index' AND tbl_name='policy_probe_foreign_translations' AND sql LIKE 'CREATE UNIQUE INDEX%'");

        indexSqlStatements.Should().HaveCount(2);
        indexSqlStatements.Should().OnlyContain(sql =>
            sql.Contains(fkColumn, StringComparison.OrdinalIgnoreCase) &&
            sql.Contains(localeColumn, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sample_ArticleTranslation_gets_its_unique_from_metadata_not_from_attributes()
    {
        typeof(ArticleTranslation).GetProperty(nameof(ArticleTranslation.ArticleId))!
            .GetCustomAttributes(typeof(SugarColumn), inherit: false)
            .Cast<SugarColumn>().Select(a => a.UniqueGroupNameList)
            .Should().AllSatisfy(g => g.Should().BeNullOrEmpty(), "the sample must not hand-write what core derives");
        typeof(ArticleTranslation).GetCustomAttributes(typeof(SugarIndexAttribute), inherit: false)
            .Should().BeEmpty("the (fk, locale) unique index already serves the lookup; the plain btree was redundant");

        using var db = new SqliteTestDatabase();
        var policy = TranslationSidecarIndexPolicy.FromMetadata(
            MetadataScanner.ScanTypes([typeof(Article), typeof(Category), typeof(Tag)]));
        var client = NewSqliteClient(db, policy);
        client.CodeFirst.InitTables(typeof(Struo.Infrastructure.Revisions.Revision), typeof(ArticleTranslation));

        var descriptor = new TranslationSidecarDescriptor(
            client.EntityMaintenance.GetTableName(typeof(ArticleTranslation)),
            client.EntityMaintenance.GetDbColumnName(nameof(ArticleTranslation.ArticleId), typeof(ArticleTranslation)),
            client.EntityMaintenance.GetDbColumnName(nameof(ArticleTranslation.Locale), typeof(ArticleTranslation)));
        var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, [descriptor], default);
        await act.Should().NotThrowAsync();
    }
}
