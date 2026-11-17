using AwesomeAssertions;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Core;
using Struo.Infrastructure.Metadata;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Utf8;

/// <summary>
/// On MySQL and MariaDB, <c>migrate</c> refuses a database or a core table whose default character set is not
/// utf8mb4, changes nothing when it does, and succeeds once the statements of its message have run.
/// </summary>
public sealed class Utf8Mb4DefaultsLiveTests
{
    public static TheoryData<string, string> Defaults => new()
    {
        { "MySql", "latin1" }, { "MySql", "utf8mb3" }, { "MariaDb", "latin1" }, { "MariaDb", "utf8mb3" },
    };

    public static TheoryData<string> Backends => LiveBackend.Names("MySql", "MariaDb");

    private static List<string> Statements(string message) =>
        message.Split(Environment.NewLine).Where(l => l.StartsWith("ALTER ", StringComparison.Ordinal)).ToList();

    private static int AppliedCount(Utf8LiveDatabase database) =>
        database.Host().GetStatus().Count(m => m.State == MigrationState.Applied);

    [LiveBackendTheory, MemberData(nameof(Defaults))]
    public async Task A_database_without_core_tables_whose_default_is_not_utf8mb4_is_refused_and_stays_empty(string backend, string characterSet)
    {
        using var server = CharacterSetDatabase.Create(backend, characterSet);
        using var database = Utf8LiveDatabase.CreateOn(backend, server.Connection);

        var failure = await Assert.ThrowsAsync<MigrationFailedException>(database.ApplyAsync);

        failure.Message.Should().Contain($"Database `{server.Name}` has the default character set {characterSet}")
            .And.Contain($"ALTER DATABASE `{server.Name}` CHARACTER SET utf8mb4;")
            .And.NotContain("ALTER TABLE `");
        database.Tables().Should().BeEmpty();
    }

    [LiveBackendTheory, MemberData(nameof(Defaults))]
    public async Task Existing_core_tables_that_default_to_the_old_character_set_are_refused_and_nothing_changes(string backend, string characterSet)
    {
        using var server = CharacterSetDatabase.Create(backend, characterSet);
        using var database = Utf8LiveDatabase.CreateOn(backend, server.Connection);
        database.ApplyLegacy();
        database.InsertSamples();
        var (structure, sets, values, applied, collations) =
            (database.Structure(), database.CharacterSets(), database.SampleValues(), AppliedCount(database), database.TableCollations());

        var failure = await Assert.ThrowsAsync<MigrationFailedException>(database.ApplyAsync);

        failure.Message.Should().Contain($"Database `{server.Name}` has the default character set {characterSet}")
            .And.Contain($"Table `{database.Prefix}users` has the default collation {characterSet}_")
            .And.Contain($"ALTER TABLE `{database.Prefix}users` CONVERT TO CHARACTER SET utf8mb4;");
        database.Structure().Should().Equal(structure);
        database.CharacterSets().Should().Equal(sets);
        database.TableCollations().Should().Equal(collations);
        database.SampleValues().Should().Equal(values);
        AppliedCount(database).Should().Be(applied);
    }

    [LiveBackendTheory, MemberData(nameof(Defaults))]
    public async Task After_the_statements_of_the_message_migrate_succeeds_and_the_columns_equal_a_fresh_database(string backend, string characterSet)
    {
        using var server = CharacterSetDatabase.Create(backend, characterSet);
        using var legacy = Utf8LiveDatabase.CreateOn(backend, server.Connection);
        using var fresh = Utf8LiveDatabase.Create(backend);
        legacy.ApplyLegacy();
        legacy.InsertSamples();
        var values = legacy.SampleValues();
        await fresh.ApplyAsync();
        var failure = await Assert.ThrowsAsync<MigrationFailedException>(legacy.ApplyAsync);

        var statements = Statements(failure.Message);
        statements.Should().Contain(s => s.StartsWith("ALTER DATABASE")).And.Contain(s => s.StartsWith("ALTER TABLE"));
        foreach (var statement in statements) legacy.Db.Ado.ExecuteCommand(statement.TrimEnd(';'));
        await legacy.ApplyAsync();

        legacy.NonUtf8Mb4Columns().Should().BeEmpty();
        legacy.SampleValues().Should().Equal(values);
        legacy.CharacterSets().Should().NotBeEmpty().And.Equal(fresh.CharacterSets());
        legacy.Structure().Should().NotBeEmpty().And.Equal(fresh.Structure());
        legacy.Host().GetStatus().Should().OnlyContain(m => m.State == MigrationState.Applied);
    }

    [LiveBackendTheory, MemberData(nameof(Backends))]
    public async Task In_a_utf8mb4_database_with_its_own_collation_the_statements_leave_every_table_in_that_collation(string backend)
    {
        const string collation = "utf8mb4_unicode_ci";
        using var server = CharacterSetDatabase.Create(backend, "utf8mb3");
        using var database = Utf8LiveDatabase.CreateOn(backend, server.Connection);
        database.ApplyLegacy();
        database.InsertSamples();
        var values = database.SampleValues();
        server.AlterDefault("utf8mb4", collation);

        var failure = await Assert.ThrowsAsync<MigrationFailedException>(database.ApplyAsync);

        var statements = Statements(failure.Message);
        statements.Should().NotBeEmpty().And.OnlyContain(s => s.StartsWith("ALTER TABLE") && s.EndsWith($"CONVERT TO CHARACTER SET utf8mb4 COLLATE {collation};"));
        failure.Message.Should().NotContain("ALTER DATABASE").And.NotContain("Database `");
        foreach (var statement in statements) database.Db.Ado.ExecuteCommand(statement.TrimEnd(';'));
        database.TableCollations().Should().OnlyContain(c => c.EndsWith(collation));
        await database.ApplyAsync();

        database.TableCollations().Should().OnlyContain(c => c.EndsWith(collation));
        database.CharacterSets().Should().NotBeEmpty().And.OnlyContain(c => c.Contains(" utf8mb4 " + collation));
        database.NonUtf8Mb4Columns().Should().BeEmpty();
        database.SampleValues().Should().Equal(values);
    }

    [LiveBackendTheory, MemberData(nameof(Backends))]
    public async Task A_utf8mb4_database_migrates_without_a_refusal(string backend)
    {
        using var server = CharacterSetDatabase.Create(backend, "utf8mb4");
        using var database = Utf8LiveDatabase.CreateOn(backend, server.Connection);
        database.ApplyLegacy();

        var applied = await database.Host().ApplyAsync(default);

        applied.Should().NotBeEmpty();
        database.NonUtf8Mb4Columns().Should().BeEmpty();
    }

    [LiveBackendTheory, MemberData(nameof(Defaults))]
    public async Task Migrating_on_startup_surfaces_the_refusal_and_applies_nothing(string backend, string characterSet)
    {
        using var server = CharacterSetDatabase.Create(backend, characterSet);
        using var database = Utf8LiveDatabase.CreateOn(backend, server.Connection);
        database.ApplyLegacy();
        var applied = AppliedCount(database);
        var options = new DatabaseOptions
        {
            DbType = database.DbType, ConnectionString = database.Connection, TablePrefix = database.Prefix, MigrateOnStartup = true,
        };

        var failure = await Assert.ThrowsAsync<MigrationFailedException>(() => StartupMigrationGate.RunAsync(
            database.Db, database.Host(), options, isDevelopment: false, FrameworkEntityTypes.All, new ListLogger(), default));

        failure.Message.Should().Contain("need utf8mb4").And.Contain($"ALTER DATABASE `{server.Name}` CHARACTER SET utf8mb4;");
        AppliedCount(database).Should().Be(applied);
    }
}
