using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Migrations.Core;

public sealed class CoreSchemaParityTests
{
    [Fact]
    public async Task The_core_migrations_build_the_schema_CodeFirst_builds()
    {
        using var migrated = new CoreMigrationHarness();
        using var codeFirst = migrated.WithPrefix($"t{Guid.NewGuid():N}"[..9] + "_");
        await migrated.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);
        codeFirst.Db().CodeFirst.InitTables(FrameworkEntityTypes.All.ToArray());

        var diffs = CoreSchemaParity.Differences(codeFirst.Db(), migrated.Db(), StruoDbType.Sqlite);

        diffs.Should().BeEmpty(string.Join(Environment.NewLine, diffs));
    }
}
