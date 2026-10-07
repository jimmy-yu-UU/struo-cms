using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class SchemaCheckerTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();

    // SqlSugar caches entity info per ConfigId (derived from the prefix), so each test uses its own.
    private ISqlSugarClient Client(string prefix) => SqlSugarClientFactory.Create(
        new DatabaseOptions
        {
            DbType = StruoDbType.Sqlite,
            ConnectionString = _file.ConnectionString,
            TablePrefix = prefix
        },
        new TestCurrentUserAccessor(Guid.Empty));

    private static SchemaCheckReport Check(ISqlSugarClient db, params Type[] types) =>
        SchemaChecker.Check(db, StruoDbType.Sqlite, types);

    public void Dispose() => _file.Dispose();

    [Fact]
    public void A_codefirst_framework_schema_has_no_errors()
    {
        var db = Client("chka_");
        db.CodeFirst.InitTables(FrameworkEntityTypes.All.ToArray());

        var report = SchemaChecker.Check(db, StruoDbType.Sqlite, FrameworkEntityTypes.All);

        report.HasErrors.Should().BeFalse(report.ToString());
    }

    [Fact]
    public void An_empty_database_reports_one_missing_table_per_type_and_creates_nothing()
    {
        var db = Client("chkb_");

        var report = SchemaChecker.Check(db, StruoDbType.Sqlite, FrameworkEntityTypes.All);

        report.Errors.Should().HaveCount(FrameworkEntityTypes.All.Count);
        report.Errors.Should().OnlyContain(f => f.Kind == FindingKind.MissingTable && f.Column == null);
        db.DbMaintenance.GetTableInfoList(false).Should().BeEmpty();
    }

    [Fact]
    public void A_missing_column_is_an_error()
    {
        var db = Client("chkc_");
        db.CodeFirst.InitTables(typeof(CheckProbeWithoutNote));

        var report = Check(db, typeof(CheckProbe));

        report.Errors.Should().ContainSingle(f => f.Kind == FindingKind.MissingColumn && f.Column == "note");
    }

    [Fact]
    public void Flipped_nullability_is_an_error()
    {
        var db = Client("chkd_");
        db.CodeFirst.InitTables(typeof(CheckProbeNullableTitle));

        var report = Check(db, typeof(CheckProbe));

        report.Errors.Should().ContainSingle(f => f.Kind == FindingKind.NullabilityMismatch && f.Column == "title");
    }

    [Fact]
    public void A_shorter_string_column_is_a_length_error()
    {
        var db = Client("chke_");
        db.CodeFirst.InitTables(typeof(CheckProbeShortTitle));

        var report = Check(db, typeof(CheckProbe));

        report.Errors.Should().ContainSingle(f => f.Kind == FindingKind.LengthMismatch && f.Column == "title");
    }

    [Fact]
    public void An_extra_database_column_is_only_a_warning()
    {
        var db = Client("chkg_");
        db.CodeFirst.InitTables(typeof(CheckProbeExtraColumn));

        var report = Check(db, typeof(CheckProbe));

        report.HasErrors.Should().BeFalse(report.ToString());
        report.Warnings.Should().ContainSingle(f => f.Kind == FindingKind.UndeclaredColumn && f.Column == "Extra");
    }

    [Fact]
    public void An_incompatible_column_type_is_an_error()
    {
        var db = Client("chki_");
        db.CodeFirst.InitTables(typeof(CheckProbeTypeShift));

        var report = Check(db, typeof(CheckProbe));

        report.Errors.Should().ContainSingle(f => f.Kind == FindingKind.TypeMismatch && f.Column == "title");
    }

    private static string RevisionTable(string prefix) => prefix + "revisions";
    private static string RevisionIndex(string prefix) => "ux_" + RevisionTable(prefix) + "_item_no";

    [Fact]
    public void A_missing_unique_index_is_an_error()
    {
        var db = Client("chkf1_");
        db.CodeFirst.InitTables(typeof(Revision));
        db.DbMaintenance.DropIndex(RevisionIndex("chkf1_"), RevisionTable("chkf1_"));

        var report = Check(db, typeof(Revision));

        report.Errors.Should().ContainSingle(f => f.Kind == FindingKind.MissingUniqueIndex);
    }

    [Fact]
    public void A_unique_index_with_another_name_on_the_same_columns_satisfies_the_check()
    {
        var db = Client("chkf2_");
        db.CodeFirst.InitTables(typeof(Revision));
        db.DbMaintenance.DropIndex(RevisionIndex("chkf2_"), RevisionTable("chkf2_"));
        db.DbMaintenance.CreateIndex(
            RevisionTable("chkf2_"), ["itemid", "collectionname", "revisionnumber"], "renamed_unique", true);

        var report = Check(db, typeof(Revision));

        report.HasErrors.Should().BeFalse(report.ToString());
    }

    [Fact]
    public void A_unique_index_with_an_extra_column_does_not_satisfy_the_check()
    {
        var db = Client("chkf3_");
        db.CodeFirst.InitTables(typeof(Revision));
        db.DbMaintenance.DropIndex(RevisionIndex("chkf3_"), RevisionTable("chkf3_"));
        db.DbMaintenance.CreateIndex(
            RevisionTable("chkf3_"), ["collectionname", "itemid", "revisionnumber", "operation"], "four_cols", true);

        var report = Check(db, typeof(Revision));

        report.Errors.Should().ContainSingle(f => f.Kind == FindingKind.MissingUniqueIndex);
    }

    [Fact]
    public void ToString_lists_one_line_per_finding_then_the_counts()
    {
        var report = new SchemaCheckReport(
        [
            new SchemaFinding(FindingSeverity.Error, FindingKind.TypeMismatch, "revisions", "title", "expected String, found int"),
            new SchemaFinding(FindingSeverity.Warning, FindingKind.UndeclaredColumn, "revisions", "extra", "no entity column"),
            new SchemaFinding(FindingSeverity.Error, FindingKind.MissingTable, "orders", null, "table is missing")
        ]);

        var lines = report.ToString().Split(Environment.NewLine);

        lines.Should().Equal(
            "ERROR revisions.title: expected String, found int",
            "WARNING revisions.extra: no entity column",
            "ERROR orders: table is missing",
            "2 error(s), 1 warning(s)");
        report.HasErrors.Should().BeTrue();
    }

    [Fact]
    public void Oracle_reports_one_unsupported_index_warning_and_no_index_errors()
    {
        var db = Client("chko_");
        var tables = EntitySchemaReader.ReadAll(db, [typeof(Revision), typeof(CheckProbe)]);

        var oracle = SchemaTableComparer.UnsupportedIndexWarnings(StruoDbType.Oracle, tables);

        oracle.Should().ContainSingle(f => f.Kind == FindingKind.IndexCheckUnsupported && f.Severity == FindingSeverity.Warning);
        SchemaTableComparer.UnsupportedIndexWarnings(StruoDbType.Sqlite, tables).Should().BeEmpty();
    }

    [Fact]
    public void Oracle_compares_columns_but_skips_type_and_length_with_one_warning()
    {
        var db = Client("chkp_");
        var table = EntitySchemaReader.Read(db, typeof(CheckProbe));
        var title = table.Columns.Single(c => c.Name == "title");
        var wrongType = new DbColumnInfo { DbColumnName = "TITLE", DataType = "NUMBER", Length = 3, IsNullable = false };

        var column = SchemaTableComparer.CompareColumn(StruoDbType.Oracle, table, title, wrongType).ToList();
        var nullability = SchemaTableComparer.CompareColumn(
            StruoDbType.Oracle, table, title, new DbColumnInfo { DbColumnName = "TITLE", DataType = "NUMBER", IsNullable = true }).ToList();

        column.Should().BeEmpty();
        nullability.Should().ContainSingle(f => f.Kind == FindingKind.NullabilityMismatch);
        var warnings = SchemaTableComparer.UnsupportedTypeWarnings(StruoDbType.Oracle, [table]);
        warnings.Should().ContainSingle(f => f.Kind == FindingKind.TypeCheckUnsupported && f.Severity == FindingSeverity.Warning);
        SchemaTableComparer.UnsupportedTypeWarnings(StruoDbType.Sqlite, [table]).Should().BeEmpty();
    }
}
