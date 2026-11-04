using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Core;

/// <summary>The language <c>code</c> carries a unique index on every backend; case-insensitive uniqueness stays in the application.</summary>
public sealed class CoreLanguageCodeIndexTests
{
    private static readonly string[] CodeColumn = ["code"];

    public static TheoryData<string> Backends => LiveBackend.Names();

    private static string NewPrefix() => "t" + Guid.NewGuid().ToString("N")[..8] + "_";

    [Fact]
    public async Task Two_languages_with_the_same_code_violate_the_unique_index()
    {
        using var harness = new CoreMigrationHarness();
        await harness.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        AssertDuplicateCodeRejected(harness.Db(), harness.Database.DbType);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Live_two_languages_with_the_same_code_violate_the_unique_index(string backend)
    {
        var (_, dbType, connection) = LiveBackend.Get(backend);
        if (connection is not { } conn) return;
        LiveDatabases.GuardDisposable(conn);
        var prefix = NewPrefix();
        var db = CoreMigrationHarness.Client(dbType, conn, prefix);
        try
        {
            await new MigrationHost(
                new MigrationHostOptions(dbType, conn, prefix, [typeof(StruoMigration).Assembly], LockTimeoutSeconds: 30)
                { NamespaceFilter = "Struo.Infrastructure.Migrations.Core", Seed = CoreMigrationHarness.Seed() },
                NullLoggerFactory.Instance).ApplyAsync(default);

            AssertDuplicateCodeRejected(db, dbType);
        }
        finally
        {
            foreach (var table in CoreSchemaParity.AllTableNames(db, prefix))
                if (db.DbMaintenance.IsAnyTable(table, false)) db.DbMaintenance.DropTable(table);
        }
    }

    private static void AssertDuplicateCodeRejected(ISqlSugarClient db, StruoDbType dbType)
    {
        var table = db.EntityMaintenance.GetTableName<Language>();
        IndexCatalog.Read(db, dbType, table).Should().ContainSingle(i => i.Columns.SequenceEqual(CodeColumn))
            .Which.IsUnique.Should().BeTrue();

        var now = DateTime.UtcNow;
        db.Insertable(new Language { Code = "dup-x", Name = "One", Sort = 90, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();

        var act = () => db.Insertable(new Language { Code = "dup-x", Name = "Two", Sort = 91, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();

        act.Should().Throw<Exception>();
        db.Queryable<Language>().Count(l => l.Code == "dup-x").Should().Be(1);
    }
}
