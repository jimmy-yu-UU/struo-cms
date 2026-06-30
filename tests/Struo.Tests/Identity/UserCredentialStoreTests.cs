using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

public class UserCredentialStoreTests
{
    private static ISqlSugarClient NewDb(SqliteTestDatabase db)
    {
        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = db.ConnectionString,
            DbType = DbType.Sqlite,
            IsAutoCloseConnection = true,
            ConfigureExternalServices = new ConfigureExternalServices
            {
                EntityService = (property, column) =>
                {
                    if (column.IsPrimarykey || column.IsIgnore) return;
                    if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
                        column.IsNullable = true;
                }
            }
        });
        client.CodeFirst.InitTables(typeof(User));
        return client;
    }

    [Fact]
    public async Task FindByEmail_returns_credential_or_null()
    {
        using var db = new SqliteTestDatabase();
        var client = NewDb(db);
        var hasher = new Argon2idPasswordHasher();
        await client.Insertable(new User
        {
            Id = Guid.CreateVersion7(), Email = "a@b.com",
            Password = hasher.Hash("pw"), IsActive = true
        }).ExecuteCommandAsync();

        var store = new SqlSugarUserCredentialStore(client);
        var found = await store.FindByEmailAsync("A@B.COM"); // case-insensitive
        found.Should().NotBeNull();
        found!.IsActive.Should().BeTrue();
        hasher.Verify(found.PasswordEncoded, "pw").Should().BeTrue();

        (await store.FindByEmailAsync("missing@b.com")).Should().BeNull();
    }
}
