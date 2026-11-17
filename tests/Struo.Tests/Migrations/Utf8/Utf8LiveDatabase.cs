using FluentMigrator.Runner;
using FluentMigrator.Runner.Generators.MySql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Core;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Migrations.Core;
using Struo.Tests.Support;

namespace Struo.Tests.Migrations.Utf8;

/// <summary>A MySQL or MariaDB database under a unique table prefix, migrated fresh or built the way the previous release built it.</summary>
internal sealed class Utf8LiveDatabase : IDisposable
{
    public const string Cjk = "\u4E2D\u6587";
    public const string Snowman = "\u2603";
    public const string Emoji = "\U0001F600";
    public const string Eszett = "\u00DF";

    private Utf8LiveDatabase(StruoDbType dbType, string connection, string prefix)
    {
        DbType = dbType;
        Connection = connection;
        Prefix = prefix;
        Db = CoreMigrationHarness.Client(dbType, connection, prefix);
    }

    public StruoDbType DbType { get; }
    public string Connection { get; }
    public string Prefix { get; }
    public ISqlSugarClient Db { get; }

    public static Utf8LiveDatabase Create(string backend)
    {
        var (_, dbType, conn) = LiveBackend.Get(backend).Configured();
        LiveDatabases.GuardDisposable(conn);
        return new Utf8LiveDatabase(dbType, conn, "t" + Guid.NewGuid().ToString("N")[..8] + "_");
    }

    /// <summary>The same, over another database of the backend, such as one from <see cref="CharacterSetDatabase"/>.</summary>
    public static Utf8LiveDatabase CreateOn(string backend, string connection)
    {
        var (_, dbType, _) = LiveBackend.Get(backend).Configured();
        LiveDatabases.GuardDisposable(connection);
        return new Utf8LiveDatabase(dbType, connection, "t" + Guid.NewGuid().ToString("N")[..8] + "_");
    }

    public MigrationHost Host(bool frameworkTypeMap = false) => new(
        new MigrationHostOptions(DbType, Connection, Prefix, [typeof(StruoMigration).Assembly], LockTimeoutSeconds: 30)
        {
            NamespaceFilter = "Struo.Infrastructure.Migrations.Core",
            Seed = CoreMigrationHarness.Seed(),
            ConfigureRunnerServices = frameworkTypeMap
                ? services => services.Replace(ServiceDescriptor.Scoped<IMySqlTypeMap>(_ => new MySql8TypeMap()))
                : null,
        },
        NullLoggerFactory.Instance);

    /// <summary>Applies the core migrations up to the release before the conversion, with the framework's own type map.</summary>
    public void ApplyLegacy()
    {
        using var provider = Host(frameworkTypeMap: true).BuildRunner(preview: false, NullLoggerFactory.Instance);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp(DropRoleCapabilities.Version);
    }

    public Task ApplyAsync() => Host().ApplyAsync(default);

    /// <summary>Columns of this database's tables whose character set is not utf8mb4.</summary>
    public List<string> NonUtf8Mb4Columns() => Tables()
        .SelectMany(t => CharacterSetCatalog.Read(Db, t).Select(c => (Table: t, Column: c)))
        .Where(x => x.Column.CharacterSet is not null && !x.Column.CharacterSet.Equals("utf8mb4", StringComparison.OrdinalIgnoreCase))
        .Select(x => $"{x.Table[Prefix.Length..]}.{x.Column.Column}={x.Column.CharacterSet}")
        .Order().ToList();

    /// <summary>Per text column: table, column, character set and collation; the prefix is stripped.</summary>
    public List<string> CharacterSets() => Tables()
        .SelectMany(t => CharacterSetCatalog.Read(Db, t).Where(c => c.CharacterSet is not null)
            .Select(c => $"{t[Prefix.Length..]}.{c.Column} {c.CharacterSet} {c.Collation}"))
        .Order().ToList();

    public List<string> Tables() => Db.DbMaintenance.GetTableInfoList(false)
        .Select(t => t.Name).Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order().ToList();

    /// <summary>Per table: column name, type, length, nullability, key and default, then each index; the prefix is stripped.</summary>
    public List<string> Structure() => Tables().SelectMany(table =>
    {
        var logical = table[Prefix.Length..];
        var columns = Db.DbMaintenance.GetColumnInfosByTableName(table, false)
            .OrderBy(c => c.DbColumnName, StringComparer.Ordinal)
            .Select(c => $"{logical}.{c.DbColumnName} {c.DataType} {c.Length} null={c.IsNullable} pk={c.IsPrimarykey} default={c.DefaultValue}");
        var indexes = IndexCatalog.Read(Db, DbType, table)
            .Select(i => $"{logical} index {i.Name.Replace(Prefix, "", StringComparison.Ordinal)} unique={i.IsUnique} ({string.Join(",", i.Columns)})");
        return columns.Concat(indexes);
    }).ToList();

    public void InsertSamples()
    {
        var now = DateTime.UtcNow;
        Db.Insertable(new Role { Id = Guid.NewGuid(), Name = "plain role", Description = "ascii", CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
        Db.Insertable(new Role { Id = Guid.NewGuid(), Name = Cjk + " role", Description = Snowman, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
        Db.Insertable(new Role { Id = Guid.NewGuid(), Name = "caf\u00E9 ", Description = null, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
        Db.Insertable(new Language { Code = "xx-" + Snowman, Name = Cjk, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
        Db.Insertable(new MediaFolder { Id = Guid.NewGuid(), Name = Snowman + Cjk, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
        Db.Insertable(new Revision
        {
            Id = Guid.NewGuid(), CollectionName = "c" + Cjk, ItemId = "i" + Snowman, RevisionNumber = 1,
            Operation = "op ", Snapshot = "{}", CreatedAt = now,
        }).ExecuteCommand();
    }

    public void InsertRole(string name)
    {
        var now = DateTime.UtcNow;
        Db.Insertable(new Role { Id = Guid.NewGuid(), Name = name, CreatedAt = now, UpdatedAt = now }).ExecuteCommand();
    }

    /// <summary>The stored text of the sample rows, in a stable order.</summary>
    public List<string> SampleValues() =>
    [
        .. Db.Queryable<Role>().ToList().Select(r => $"role|{r.Name}|{r.Description ?? "<null>"}").Order(),
        .. Db.Queryable<Language>().ToList().Select(l => $"language|{l.Code}|{l.Name}").Order(),
        .. Db.Queryable<MediaFolder>().ToList().Select(f => $"folder|{f.Name}").Order(),
        .. Db.Queryable<Revision>().ToList().Select(r => $"revision|{r.CollectionName}|{r.ItemId}|{r.Operation}").Order(),
    ];

    public void Dispose()
    {
        foreach (var table in CoreSchemaParity.AllTableNames(Db, Prefix))
            if (Db.DbMaintenance.IsAnyTable(table, false)) Db.DbMaintenance.DropTable(table);
    }
}
