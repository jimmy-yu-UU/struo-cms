using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Core;

/// <summary>Users without an access token coexist, and a token hash finds exactly its user.</summary>
public sealed class CoreUserAccessTokenTests
{
    private static readonly string[] TokenColumn = ["accesstoken"];

    public static TheoryData<string> Backends => LiveBackend.Names();

    private static string NewPrefix() => "t" + Guid.NewGuid().ToString("N")[..8] + "_";

    [Fact]
    public async Task Migrated_users_without_a_token_coexist_and_the_token_index_is_not_unique()
    {
        using var harness = new CoreMigrationHarness();
        await harness.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        await AssertTokenBehaviour(harness.Db(), harness.Database.DbType);
    }

    [Theory, MemberData(nameof(Backends))]
    public async Task Live_migrated_users_without_a_token_coexist_and_the_token_index_is_not_unique(string backend)
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

            await AssertTokenBehaviour(db, dbType);
        }
        finally
        {
            foreach (var table in CoreSchemaParity.AllTableNames(db, prefix))
                if (db.DbMaintenance.IsAnyTable(table, false)) db.DbMaintenance.DropTable(table);
        }
    }

    private static async Task AssertTokenBehaviour(ISqlSugarClient db, StruoDbType dbType)
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        await db.Insertable(new User { Id = first, Email = "a@corp.test", Password = "x", IsActive = true }).ExecuteCommandAsync();
        await db.Insertable(new User { Id = second, Email = "b@corp.test", Password = "x", IsActive = true }).ExecuteCommandAsync();

        var table = db.EntityMaintenance.GetTableName<User>();
        IndexCatalog.Read(db, dbType, table).Should().ContainSingle(i => i.Columns.SequenceEqual(TokenColumn))
            .Which.IsUnique.Should().BeFalse();

        var external = new SqlSugarExternalUserStore(db);
        var jitOne = await external.CreateExternalUserAsync("jit1@corp.test", "One");
        var jitTwo = await external.CreateExternalUserAsync("jit2@corp.test", "Two");
        jitOne.Should().NotBe(jitTwo);

        const string hash = "ABCDEF0123456789";
        await db.Updateable<User>().SetColumns(u => u.AccessToken == hash).Where(u => u.Id == second).ExecuteCommandAsync();

        var found = await new SqlSugarUserCredentialStore(db).FindByAccessTokenAsync(hash);
        found.Should().NotBeNull();
        found!.Id.Should().Be(second);
        (await new SqlSugarUserCredentialStore(db).FindByAccessTokenAsync("missing")).Should().BeNull();
        (await db.Queryable<User>().Where(u => u.Email.EndsWith("@corp.test")).CountAsync()).Should().Be(4);
    }
}
