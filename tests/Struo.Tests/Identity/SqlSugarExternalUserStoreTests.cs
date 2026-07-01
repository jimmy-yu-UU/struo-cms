using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

public class SqlSugarExternalUserStoreTests
{
    private static ISqlSugarClient NewDb(SqliteTestDatabase file)
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables(typeof(User));
        return db;
    }

    [Fact]
    public async Task FindByEmail_is_case_insensitive_and_returns_active_flag()
    {
        using var file = new SqliteTestDatabase();
        var db = NewDb(file);
        var id = Guid.CreateVersion7();
        await db.Insertable(new User { Id = id, Email = "Alice@Corp.com", Password = "x", IsActive = false }).ExecuteCommandAsync();

        var store = new SqlSugarExternalUserStore(db);
        var match = await store.FindByEmailAsync("alice@corp.com");

        match.Should().NotBeNull();
        match!.Id.Should().Be(id);
        match.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task FindByEmail_returns_null_when_absent()
    {
        using var file = new SqliteTestDatabase();
        var db = NewDb(file);
        var store = new SqlSugarExternalUserStore(db);
        (await store.FindByEmailAsync("nobody@corp.com")).Should().BeNull();
    }

    [Fact]
    public async Task CreateExternalUser_persists_active_roleless_passwordless_user()
    {
        using var file = new SqliteTestDatabase();
        var db = NewDb(file);
        var store = new SqlSugarExternalUserStore(db);

        var newId = await store.CreateExternalUserAsync("new@corp.com", "New Person");

        var row = await db.Queryable<User>().Where(u => u.Id == newId).FirstAsync();
        row.Should().NotBeNull();
        row!.Email.Should().Be("new@corp.com");
        row.Name.Should().Be("New Person");
        row.IsActive.Should().BeTrue();
        row.Password.Should().BeEmpty();
        row.AccessToken.Should().BeNull();
    }
}
