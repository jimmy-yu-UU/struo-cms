using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Migrations.Utf8;

/// <summary>
/// <c>ALTER TABLE ... CONVERT TO CHARACTER SET utf8mb4</c> turns a <c>TEXT</c> column into <c>MEDIUMTEXT</c>
/// on MySQL and MariaDB; the schema check warns about the character set before and reports nothing after.
/// </summary>
public sealed class Utf8Mb4TextWideningLiveTests(ITestOutputHelper output)
{
    public static TheoryData<string, string> Defaults => new()
    {
        { "MySql", "latin1" }, { "MySql", "utf8mb3" }, { "MariaDb", "latin1" }, { "MariaDb", "utf8mb3" },
    };

    [SugarTable("wide_notes")]
    private sealed class WideNote
    {
        [SugarColumn(IsPrimaryKey = true, Length = 36)] public string Id { get; set; } = "";
        [SugarColumn(Length = 10000)] public string Body { get; set; } = "";
    }

    [LiveBackendTheory, MemberData(nameof(Defaults))]
    public void A_text_column_becomes_mediumtext_and_the_schema_check_has_no_finding_for_it_afterwards(string backend, string characterSet)
    {
        using var server = CharacterSetDatabase.Create(backend, characterSet);
        using var database = Utf8LiveDatabase.CreateOn(backend, server.Connection);
        const string table = "wide_notes";
        database.Db.Ado.ExecuteCommand(
            $"CREATE TABLE `{table}` (`Id` VARCHAR(36) NOT NULL PRIMARY KEY, `Body` TEXT NOT NULL) CHARACTER SET {characterSet}");
        var before = ColumnType(database, table);
        var findingsBefore = Findings(database);

        database.Db.Ado.ExecuteCommand($"ALTER TABLE `{table}` CONVERT TO CHARACTER SET utf8mb4");

        var after = ColumnType(database, table);
        var findingsAfter = Findings(database);
        output.WriteLine($"type: {before} -> {after}");
        output.WriteLine("check before: " + string.Join(" | ", findingsBefore));
        output.WriteLine("check after: " + string.Join(" | ", findingsAfter));
        before.Should().Be("text");
        after.Should().Be("mediumtext");
        findingsBefore.Should().Contain(f => f.Contains("NonUtf8Mb4String body"));
        findingsAfter.Should().BeEmpty();
    }

    private static string ColumnType(Utf8LiveDatabase database, string table) =>
        database.Db.DbMaintenance.GetColumnInfosByTableName(table, false).Single(c => c.DbColumnName == "Body").DataType.ToLowerInvariant();

    private static List<string> Findings(Utf8LiveDatabase database) =>
        SchemaChecker.Check(database.Db, database.DbType, [typeof(WideNote)]).Findings
            .Select(f => $"{f.Severity} {f.Kind} {f.Column}: {f.Message}").ToList();
}
