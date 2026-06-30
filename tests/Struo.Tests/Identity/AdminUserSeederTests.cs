using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

public class AdminUserSeederTests
{
    private static ISqlSugarClient NewDb(SqliteTestDatabase db)
    {
        var c = new SqlSugarClient(new ConnectionConfig
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
        c.CodeFirst.InitTables(typeof(User));
        return c;
    }

    [Fact]
    public async Task Seeds_admin_when_no_users_and_is_idempotent()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);
        var hasher = new Argon2idPasswordHasher();

        await AdminUserSeeder.SeedAsync(db, hasher, "admin@struo.local", "admin12345678");
        (await db.Queryable<User>().CountAsync()).Should().Be(1);

        await AdminUserSeeder.SeedAsync(db, hasher, "admin@struo.local", "admin12345678");
        (await db.Queryable<User>().CountAsync()).Should().Be(1); // no duplicate
    }

    [Fact]
    public async Task Does_nothing_when_config_missing()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);
        await AdminUserSeeder.SeedAsync(db, new Argon2idPasswordHasher(), null, null);
        (await db.Queryable<User>().CountAsync()).Should().Be(0);
    }
}
