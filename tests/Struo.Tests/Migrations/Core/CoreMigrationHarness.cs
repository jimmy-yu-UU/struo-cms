using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;

namespace Struo.Tests.Migrations.Core;

/// <summary>Applies the core migrations to a fresh SQLite file under a unique table prefix.</summary>
internal sealed class CoreMigrationHarness : IDisposable
{
    private readonly SqliteTestDatabase _file;
    private readonly bool _ownsFile;

    public CoreMigrationHarness() : this(new SqliteTestDatabase(), $"t{Guid.NewGuid():N}"[..9] + "_", true) { }

    private CoreMigrationHarness(SqliteTestDatabase file, string prefix, bool ownsFile) =>
        (_file, Prefix, _ownsFile) = (file, prefix, ownsFile);

    public string Prefix { get; }

    /// <summary>A harness over the same database file under another prefix; it does not delete the file.</summary>
    public CoreMigrationHarness WithPrefix(string prefix) => new(_file, prefix, false);

    public void Dispose()
    {
        if (_ownsFile) _file.Dispose();
    }

    public MigrationHostOptions HostOptions(CoreSeedData? seed) =>
        new(StruoDbType.Sqlite, _file.ConnectionString, Prefix,
            [typeof(StruoMigration).Assembly], LockTimeoutSeconds: 5)
        { NamespaceFilter = "Struo.Infrastructure.Migrations.Core", Seed = seed };

    public MigrationHost Host(CoreSeedData? seed) => new(HostOptions(seed), NullLoggerFactory.Instance);

    public ISqlSugarClient Db()
    {
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(FileTranslation)] = TranslationSidecarIndexPolicy.KeyFor(typeof(FileTranslation), "FileId", "Locale")
        });
        return SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString, TablePrefix = Prefix },
            new TestCurrentUserAccessor(Guid.Empty),
            policy);
    }

    public static CoreSeedData Seed(
        string? email = "admin@example.com", string? password = "s3cret-pw",
        IReadOnlyList<string>? publicRead = null, IPasswordHasher? hasher = null) =>
        CoreSeedData.From(new LocalizationOptions(), hasher ?? new Argon2idPasswordHasher(), email, password, publicRead ?? []);
}
