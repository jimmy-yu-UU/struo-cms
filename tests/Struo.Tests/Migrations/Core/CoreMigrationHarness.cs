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

/// <summary>Applies the core migrations to a fresh unit database under a unique table prefix.</summary>
internal sealed class CoreMigrationHarness : IDisposable
{
    private readonly bool _ownsDatabase;

    public CoreMigrationHarness() : this(TestBackend.CreateUnitDatabase(), $"t{Guid.NewGuid():N}"[..9] + "_", true) { }

    private CoreMigrationHarness(ITestDatabase database, string prefix, bool ownsDatabase) =>
        (Database, Prefix, _ownsDatabase) = (database, prefix, ownsDatabase);

    public ITestDatabase Database { get; }

    public string Prefix { get; }

    /// <summary>A harness over the same database under another prefix; it does not dispose the database.</summary>
    public CoreMigrationHarness WithPrefix(string prefix) => new(Database, prefix, false);

    public void Dispose()
    {
        if (_ownsDatabase) Database.Dispose();
    }

    public MigrationHostOptions HostOptions(CoreSeedData? seed) =>
        new(Database.DbType, Database.ConnectionString, Prefix,
            [typeof(StruoMigration).Assembly], LockTimeoutSeconds: 5)
        { NamespaceFilter = "Struo.Infrastructure.Migrations.Core", Seed = seed };

    public MigrationHost Host(CoreSeedData? seed) => new(HostOptions(seed), NullLoggerFactory.Instance);

    public ISqlSugarClient Db() => Client(Database.DbType, Database.ConnectionString, Prefix);

    /// <summary>A client with the file-translation sidecar policy, on any backend and prefix.</summary>
    public static ISqlSugarClient Client(StruoDbType dbType, string connectionString, string prefix)
    {
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(FileTranslation)] = TranslationSidecarIndexPolicy.KeyFor(typeof(FileTranslation), "FileId", "Locale")
        });
        return SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = dbType, ConnectionString = connectionString, TablePrefix = prefix },
            new TestCurrentUserAccessor(Guid.Empty),
            policy);
    }

    public static CoreSeedData Seed(
        string? email = "admin@example.com", string? password = "s3cret-pw",
        IReadOnlyList<string>? publicRead = null, IPasswordHasher? hasher = null) =>
        CoreSeedData.From(new LocalizationOptions(), hasher ?? new Argon2idPasswordHasher(), email, password, publicRead ?? []);
}
