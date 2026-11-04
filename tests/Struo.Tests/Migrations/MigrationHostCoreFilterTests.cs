using AwesomeAssertions;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Core;
using Xunit;

namespace Struo.Tests.Migrations;

/// <summary>
/// The baseline treats a migration as core by its assembly and its namespace prefix, so a core migration in
/// a sub-namespace is still core.
/// </summary>
public sealed class MigrationHostCoreFilterTests
{
    [Theory]
    [InlineData("Struo.Infrastructure.Migrations.Core", true)]
    [InlineData("Struo.Infrastructure.Migrations.Core.Data", true)]
    [InlineData("Struo.Infrastructure.Migrations.CoreExtras", false)]
    [InlineData("Struo.Infrastructure.Migrations", false)]
    [InlineData("Struo.Tests.Migrations.Core", false)]
    [InlineData(null, false)]
    public void Core_namespace_is_the_core_namespace_or_one_nested_in_it(string? ns, bool expected) =>
        MigrationHost.IsCoreNamespace(ns).Should().Be(expected);

    [Fact]
    public void Every_core_assembly_migration_counts_as_core()
    {
        var migrations = typeof(SeedCoreData).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(StruoMigration).IsAssignableFrom(t))
            .ToList();

        migrations.Should().NotBeEmpty();
        migrations.Should().OnlyContain(t => MigrationHost.IsCoreMigration(t));
    }

    [Fact]
    public void A_migration_from_another_assembly_is_not_core() =>
        MigrationHost.IsCoreMigration(typeof(Struo.Infrastructure.Migrations.Core.ContentProbe.CreateContentProbe))
            .Should().BeFalse();
}
