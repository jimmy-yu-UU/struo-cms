using AwesomeAssertions;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Utf8;

public sealed class Utf8Mb4LiveTests
{
    public static TheoryData<string> Backends => LiveBackend.Names("MySql", "MariaDb");

    private const int CoreStringColumns = 22;
    private const int EntityStringColumns = 21;

    [LiveBackendTheory, MemberData(nameof(Backends))]
    public async Task A_fresh_migrated_database_has_no_string_column_outside_utf8mb4(string backend)
    {
        using var database = Utf8LiveDatabase.Create(backend);

        await database.ApplyAsync();

        database.NonUtf8Mb4Columns().Should().BeEmpty();
        database.Tables().Should().HaveCountGreaterThan(5);
    }

    [LiveBackendTheory, MemberData(nameof(Backends))]
    public void The_database_built_by_the_previous_release_has_the_22_columns_outside_utf8mb4(string backend)
    {
        using var database = Utf8LiveDatabase.Create(backend);

        database.ApplyLegacy();

        database.NonUtf8Mb4Columns().Should().HaveCount(CoreStringColumns)
            .And.OnlyContain(c => c.EndsWith("=utf8mb3") || c.EndsWith("=utf8"));
    }

    [LiveBackendTheory, MemberData(nameof(Backends))]
    public async Task Upgrading_converts_every_column_and_keeps_data_keys_nullability_and_defaults(string backend)
    {
        using var legacy = Utf8LiveDatabase.Create(backend);
        using var fresh = Utf8LiveDatabase.Create(backend);
        legacy.ApplyLegacy();
        legacy.InsertSamples();
        var valuesBefore = legacy.SampleValues();
        var structureBefore = legacy.Structure();

        await legacy.ApplyAsync();
        await fresh.ApplyAsync();

        legacy.NonUtf8Mb4Columns().Should().BeEmpty();
        legacy.SampleValues().Should().Equal(valuesBefore);
        valuesBefore.Should().Contain($"role|{Utf8LiveDatabase.Cjk} role|{Utf8LiveDatabase.Snowman}");
        legacy.Structure().Should().Equal(structureBefore);
        legacy.Structure().Should().Equal(fresh.Structure());
        legacy.CharacterSets().Should().NotBeEmpty().And.Equal(fresh.CharacterSets());
    }

    [LiveBackendTheory, MemberData(nameof(Backends))]
    public async Task A_supplementary_plane_character_is_stored_and_compared_on_migrated_and_upgraded_tables(string backend)
    {
        using var fresh = Utf8LiveDatabase.Create(backend);
        using var legacy = Utf8LiveDatabase.Create(backend);
        legacy.ApplyLegacy();
        await fresh.ApplyAsync();
        await legacy.ApplyAsync();

        foreach (var database in new[] { fresh, legacy })
        {
            var name = "grin " + Utf8LiveDatabase.Emoji;
            database.InsertRole(name);

            database.Db.Queryable<Role>().Where(r => r.Name == name).Select(r => r.Name).ToList().Should().Equal(name);
            database.Db.Queryable<Role>().Where(r => r.Name.Contains(Utf8LiveDatabase.Emoji)).Count().Should().Be(1);
        }
    }

    [LiveBackendTheory, MemberData(nameof(Backends))]
    public async Task The_schema_check_warns_for_each_legacy_column_and_is_silent_on_a_fresh_database(string backend)
    {
        using var legacy = Utf8LiveDatabase.Create(backend);
        using var fresh = Utf8LiveDatabase.Create(backend);
        legacy.ApplyLegacy();
        await fresh.ApplyAsync();

        var warned = SchemaChecker.Check(legacy.Db, legacy.DbType, FrameworkEntityTypes.All);
        var clean = SchemaChecker.Check(fresh.Db, fresh.DbType, FrameworkEntityTypes.All);

        warned.HasErrors.Should().BeFalse(warned.ToString());
        warned.GetWarnings().Where(f => f.Kind == FindingKind.NonUtf8Mb4String).Should().HaveCount(EntityStringColumns)
            .And.OnlyContain(f => f.Table.StartsWith(legacy.Prefix) && f.Message.Contains("utf8mb3"));
        clean.HasErrors.Should().BeFalse(clean.ToString());
        clean.Findings.Should().NotContain(f => f.Kind == FindingKind.NonUtf8Mb4String);
    }
}
