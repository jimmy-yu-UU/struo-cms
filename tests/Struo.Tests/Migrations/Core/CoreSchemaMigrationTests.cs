using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations.Core;
using Struo.Infrastructure.Migrations.Schema;
using Xunit;

namespace Struo.Tests.Migrations.Core;

public sealed class CoreSchemaMigrationTests : IDisposable
{
    private readonly CoreMigrationHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Core_migrations_apply_in_order_and_are_recorded()
    {
        var applied = await _h.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        applied.Select(m => m.Version).Should().Equal(CreateCoreSchema.Version, SeedCoreData.Version);
    }

    [Fact]
    public async Task Every_framework_table_exists_under_the_prefix()
    {
        await _h.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);
        var db = _h.Db();

        var tables = db.DbMaintenance.GetTableInfoList(false).Select(t => t.Name.ToLowerInvariant()).ToHashSet();

        foreach (var type in FrameworkEntityTypes.All)
            tables.Should().Contain(db.EntityMaintenance.GetTableName(type).ToLowerInvariant());
        tables.Should().OnlyContain(t => t.StartsWith(_h.Prefix) || t == "sqlite_sequence");
    }

    [Fact]
    public async Task Migrated_schema_satisfies_the_schema_checker_for_every_framework_entity()
    {
        await _h.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        var report = SchemaChecker.Check(_h.Db(), StruoDbType.Sqlite, FrameworkEntityTypes.All);

        report.HasErrors.Should().BeFalse(report.ToString());
    }

    [Fact]
    public async Task Core_migrations_apply_under_two_prefixes_in_one_database()
    {
        await _h.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);
        using var second = _h.WithPrefix("u" + Guid.NewGuid().ToString("N")[..8] + "_");

        var applied = await second.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        applied.Should().HaveCount(2);
    }
}
