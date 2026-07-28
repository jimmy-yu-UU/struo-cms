using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class DataSeederTests
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
                    // SQLite-only: identity PK must be INTEGER (rowid alias) to auto-increment.
                    // Mirrors SqlSugarClientFactory's real-code rewrite — Language.Id is `long
                    // IsIdentity`, and without this SQLite rejects the CodeFirst DDL with
                    // "AUTOINCREMENT is only allowed on an INTEGER PRIMARY KEY".
                    if (column.IsPrimarykey && column.IsIdentity)
                    {
                        column.DataType = "INTEGER";
                    }

                    if (column.IsPrimarykey || column.IsIgnore) return;
                    if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
                        column.IsNullable = true;
                }
            }
        });
        c.CodeFirst.InitTables(typeof(Language), typeof(User), typeof(Role), typeof(Permission), typeof(UserRole));
        return c;
    }

    private static readonly IPasswordHasher Hasher = new Argon2idPasswordHasher();
    private static readonly string[] NoCollections = [];

    [Fact]
    public async Task Fires_seeder_when_trigger_table_created_this_run()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);
        // Tables exist now; existingBefore is empty => every trigger table was "just created".
        var existingBefore = new HashSet<string>(StringComparer.Ordinal);

        await DataSeeder.SeedAsync(db, existingBefore, Hasher,
            "admin@admin.com", "admin", NoCollections, isProduction: false, NullLogger.Instance);

        (await db.Queryable<Language>().CountAsync()).Should().Be(2);
        (await db.Queryable<User>().CountAsync()).Should().Be(1);
        (await db.Queryable<Role>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Skips_seeder_when_trigger_table_preexisted_even_if_empty()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);
        // Trigger tables were present BEFORE this run => skip, despite being empty.
        var existingBefore = DataSeeder.GetTableNames(db);

        await DataSeeder.SeedAsync(db, existingBefore, Hasher,
            "admin@admin.com", "admin", NoCollections, isProduction: false, NullLogger.Instance);

        (await db.Queryable<Language>().CountAsync()).Should().Be(0);
        (await db.Queryable<User>().CountAsync()).Should().Be(0);
        (await db.Queryable<Role>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Second_run_seeds_nothing_no_duplicates()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);

        await DataSeeder.SeedAsync(db, new HashSet<string>(StringComparer.Ordinal), Hasher,
            "admin@admin.com", "admin", NoCollections, isProduction: false, NullLogger.Instance);
        // Second boot: tables now pre-exist.
        await DataSeeder.SeedAsync(db, DataSeeder.GetTableNames(db), Hasher,
            "admin@admin.com", "admin", NoCollections, isProduction: false, NullLogger.Instance);

        (await db.Queryable<Language>().CountAsync()).Should().Be(2);
        (await db.Queryable<User>().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Seeds_in_production_environment()
    {
        using var dbf = new SqliteTestDatabase();
        var db = NewDb(dbf);

        await DataSeeder.SeedAsync(db, new HashSet<string>(StringComparer.Ordinal), Hasher,
            "admin@admin.com", "s3cret-not-default", NoCollections, isProduction: true, NullLogger.Instance);

        (await db.Queryable<User>().CountAsync()).Should().Be(1); // not gated out by environment
    }
}
