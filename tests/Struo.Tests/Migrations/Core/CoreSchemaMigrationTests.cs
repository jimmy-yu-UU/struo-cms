using AwesomeAssertions;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
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

        applied.Select(m => m.Version).Should().Equal(CreateCoreSchema.Version, SeedCoreData.Version, AddLanguageCodeUniqueIndex.Version, CreateRoleCapabilities.Version, DropRoleCapabilities.Version);
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

        var report = SchemaChecker.Check(_h.Db(), _h.Database.DbType, FrameworkEntityTypes.All);

        report.HasErrors.Should().BeFalse(report.ToString());
    }

    [Fact]
    public async Task Core_migrations_apply_under_two_prefixes_in_one_database()
    {
        await _h.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);
        using var second = _h.WithPrefix("u" + Guid.NewGuid().ToString("N")[..8] + "_");

        var applied = await second.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        applied.Should().HaveCount(5);
    }

    [Fact]
    public async Task Role_capabilities_table_does_not_exist_after_the_full_migrate()
    {
        await _h.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        _h.Db().DbMaintenance.IsAnyTable(RoleCapabilitiesTable, false).Should().BeFalse();
    }

    [Fact]
    public async Task Rolling_back_the_drop_recreates_the_role_capabilities_table_and_its_unique_index()
    {
        var host = _h.Host(CoreMigrationHarness.Seed());
        await host.ApplyAsync(default);

        MigrateDownTo(host, CreateRoleCapabilities.Version);

        var db = _h.Db();
        db.DbMaintenance.IsAnyTable(RoleCapabilitiesTable, false).Should().BeTrue();
        db.DbMaintenance.GetColumnInfosByTableName(RoleCapabilitiesTable, false)
            .Select(c => c.DbColumnName.ToLowerInvariant())
            .Should().BeEquivalentTo("id", "roleid", "capability", "createdat", "createdby", "updatedat", "updatedby", "version");
        if (IndexCatalog.IsSupported(_h.Database.DbType))
            IndexCatalog.Read(db, _h.Database.DbType, RoleCapabilitiesTable)
                .Should().ContainSingle(i => i.Name == $"ux_{RoleCapabilitiesTable}_role_capability")
                .Which.Should().Match<DbIndex>(i => i.IsUnique && i.Columns.SequenceEqual(RoleCapabilityKey));
    }

    [Fact]
    public async Task Applying_again_after_the_rollback_drops_the_table_once_more()
    {
        var host = _h.Host(CoreMigrationHarness.Seed());
        await host.ApplyAsync(default);
        MigrateDownTo(host, CreateRoleCapabilities.Version);

        var applied = await host.ApplyAsync(default);

        applied.Select(m => m.Version).Should().Equal(DropRoleCapabilities.Version);
        _h.Db().DbMaintenance.IsAnyTable(RoleCapabilitiesTable, false).Should().BeFalse();
    }

    private string RoleCapabilitiesTable => _h.Prefix + "role_capabilities";

    private static readonly string[] RoleCapabilityKey = ["roleid", "capability"];

    private static void MigrateDownTo(MigrationHost host, long version)
    {
        using var sp = host.BuildRunner(preview: false, NullLoggerFactory.Instance);
        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(version);
    }
}
