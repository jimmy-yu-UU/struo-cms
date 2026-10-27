using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Files;

[Collection("MigrationCli")]
public sealed class FilesSweepCommandTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tester = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MemoryStorage : IFileStorage
    {
        public readonly Dictionary<string, DateTime> Blobs = [];
        public readonly HashSet<string> FailDelete = [];
        public string? Description { get; set; }

        public string Describe() => Description ?? nameof(MemoryStorage);

        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            if (FailDelete.Contains(key)) throw new IOException("storage offline");
            Blobs.Remove(key);
            return Task.CompletedTask;
        }

        public IAsyncEnumerable<StoredBlob> ListAsync(CancellationToken ct = default) =>
            Blobs.ToArray().Select(b => new StoredBlob(b.Key, b.Value)).ToAsyncEnumerable();

        public bool SupportsPresignedUrls => false;
        public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
    }

    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();
    private readonly MemoryStorage _storage = new();
    private readonly ServiceProvider _sp;
    private readonly ISqlSugarClient _db;
    private readonly TimeProvider _previousClock = MigrationCli.Clock;

    public FilesSweepCommandTests()
    {
        _db = SqlSugarClientFactory.Create(_database.Options(), new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<File>();
        _sp = Services(new FileStorageOptions(), _database.Options());
        MigrationCli.Clock = new FixedClock();
    }

    // The sweep reads the options only to describe its targets; the storage and the database client it works
    // on are the fake and the real test database registered here.
    private ServiceProvider Services(FileStorageOptions files, DatabaseOptions summaryDb) =>
        new ServiceCollection()
            .AddSingleton<IFileStorage>(_storage)
            .AddSingleton(files)
            .AddSingleton(Options.Create(summaryDb))
            .AddSingleton<ISqlSugarClient>(_ => SqlSugarClientFactory.Create(
                _database.Options(), new TestCurrentUserAccessor(Tester)))
            .BuildServiceProvider();

    public void Dispose()
    {
        MigrationCli.Clock = _previousClock;
        _sp.Dispose();
        _database.Dispose();
    }

    private static string Key(int n, string ext = ".png") => $"2026/09/{n:x32}{ext}";
    private static DateTime Old => Now.UtcDateTime.AddHours(-48);
    private static DateTime Young => Now.UtcDateTime.AddHours(-2);

    private void Row(string key, bool trashed = false) =>
        _db.Insertable(new File
        {
            Id = Guid.NewGuid(), StorageKey = key, FileName = "x", ContentType = "image/png",
            DeletedAt = trashed ? Now.UtcDateTime : null,
        }).ExecuteCommand();

    private async Task<(int Code, string Out, string Err)> Run(params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await MigrationCli.RunAsync(MigrationCli.Parse(["files:sweep", .. args]), _sp, o, e, default);
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public async Task Dry_run_lists_old_orphans_and_deletes_nothing()
    {
        _storage.Blobs[Key(1)] = Old;

        var (code, output, err) = await Run();

        code.Should().Be(MigrationCli.Success);
        err.Should().BeEmpty();
        output.Should().Contain(Key(1)).And.Contain("1 of 1 eligible");
        _storage.Blobs.Should().ContainKey(Key(1));
    }

    [Fact]
    public async Task Apply_deletes_old_orphans_only()
    {
        _storage.Blobs[Key(1)] = Old;
        _storage.Blobs[Key(2)] = Old;
        Row(Key(2));

        var (code, output, _) = await Run("--apply");

        code.Should().Be(MigrationCli.Success);
        output.Should().Contain("1 deleted");
        _storage.Blobs.Keys.Should().BeEquivalentTo([Key(2)]);
    }

    [Fact]
    public async Task A_blob_younger_than_the_grace_period_is_kept()
    {
        _storage.Blobs[Key(1)] = Young;

        var (code, output, _) = await Run("--apply");

        code.Should().Be(MigrationCli.Success);
        output.Should().Contain("0 of 0 eligible").And.Contain("1 younger");
        _storage.Blobs.Should().ContainKey(Key(1));
    }

    [Fact]
    public async Task Min_age_hours_moves_the_grace_period()
    {
        _storage.Blobs[Key(1)] = Young;

        var (_, kept, _) = await Run("--min-age-hours=72", "--apply");
        kept.Should().Contain("0 of 0 eligible");
        _storage.Blobs.Should().ContainKey(Key(1));

        var (_, output, _) = await Run("--min-age-hours", "1");
        output.Should().Contain(Key(1));
    }

    [Fact]
    public async Task A_blob_with_an_unknown_age_is_kept()
    {
        _storage.Blobs[Key(1)] = DateTime.MaxValue;

        var (_, output, _) = await Run("--apply", "--force");

        output.Should().Contain("0 of 0 eligible").And.Contain("1 younger");
        _storage.Blobs.Should().ContainKey(Key(1));
    }

    [Fact]
    public async Task A_blob_of_a_trashed_row_is_not_an_orphan()
    {
        _storage.Blobs[Key(1)] = Old;
        Row(Key(1), trashed: true);

        var (_, output, _) = await Run("--apply");

        output.Should().Contain("0 of 1 eligible");
        _storage.Blobs.Should().ContainKey(Key(1));
    }

    [Fact]
    public async Task Keys_that_are_not_storage_keys_are_ignored()
    {
        var cacheKey = "image-cache/ab/" + new string('a', 64);
        _storage.Blobs[cacheKey] = Old;
        _storage.Blobs["2026/09/readme.txt"] = Old;
        _storage.Blobs[Key(3, "")] = Old;
        _storage.Blobs[Key(4)] = Old;
        Row(Key(4));

        var (_, output, _) = await Run("--apply");

        output.Should().Contain("1 deleted");
        _storage.Blobs.Keys.Should().BeEquivalentTo([cacheKey, "2026/09/readme.txt", Key(4)]);
    }

    [Fact]
    public async Task More_orphans_than_one_lookup_batch_are_all_found()
    {
        for (var i = 1; i <= 1200; i++) _storage.Blobs[Key(i)] = Old;
        for (var i = 1; i <= 1200; i += 2) Row(Key(i));

        var (_, output, _) = await Run("--apply");

        output.Should().Contain("600 deleted");
        _storage.Blobs.Should().HaveCount(600);
    }

    [Fact]
    public async Task A_failed_delete_is_reported_and_exits_with_failure()
    {
        _storage.Blobs[Key(1)] = Old;
        _storage.Blobs[Key(2)] = Old;
        _storage.FailDelete.Add(Key(1));

        var (code, output, err) = await Run("--apply", "--force");

        code.Should().Be(MigrationCli.Failure);
        err.Should().Contain(Key(1)).And.Contain("storage offline");
        output.Should().Contain("1 deleted").And.Contain("1 failed");
        _storage.Blobs.Keys.Should().BeEquivalentTo([Key(1)]);
    }

    private void Seed(int orphans, int referenced)
    {
        for (var i = 1; i <= orphans; i++) _storage.Blobs[Key(i)] = Old;
        for (var i = 1; i <= referenced; i++)
        {
            _storage.Blobs[Key(1000 + i)] = Old;
            Row(Key(1000 + i));
        }
    }

    [Fact]
    public async Task Apply_refuses_when_more_than_half_of_the_eligible_blobs_are_orphans()
    {
        Seed(orphans: 3, referenced: 2);

        var (code, output, err) = await Run("--apply");

        code.Should().Be(MigrationCli.Refused);
        _storage.Blobs.Should().HaveCount(5);
        err.Should().Contain("3 of 5").And.Contain("--force");
        output.Should().Contain("storage:").And.Contain("database:");
    }

    [Fact]
    public async Task Force_overrides_the_refusal()
    {
        Seed(orphans: 3, referenced: 2);

        var (code, output, _) = await Run("--apply", "--force");

        code.Should().Be(MigrationCli.Success);
        output.Should().Contain("3 deleted");
        _storage.Blobs.Should().HaveCount(2);
    }

    [Fact]
    public async Task Exactly_half_is_below_the_threshold()
    {
        Seed(orphans: 2, referenced: 2);

        var (code, _, _) = await Run("--apply");

        code.Should().Be(MigrationCli.Success);
        _storage.Blobs.Should().HaveCount(2);
    }

    [Fact]
    public async Task Dry_run_reports_the_ratio_and_that_apply_would_need_force()
    {
        Seed(orphans: 3, referenced: 1);

        var (code, output, _) = await Run();

        code.Should().Be(MigrationCli.Success);
        output.Should().Contain("3 of 4").And.Contain("75%").And.Contain("--apply would need --force");
        _storage.Blobs.Should().HaveCount(4);
    }

    [Fact]
    public async Task Dry_run_below_the_threshold_does_not_mention_force()
    {
        Seed(orphans: 1, referenced: 3);

        var (_, output, _) = await Run();

        output.Should().NotContain("--force");
    }

    [Fact]
    public async Task Both_modes_print_the_local_storage_root_and_the_database_before_the_orphans()
    {
        var files = new FileStorageOptions { Backend = "local", Local = { RootPath = "/data/uploads" } };
        var db = new DatabaseOptions
            { DbType = StruoDbType.PostgreSQL, ConnectionString = "Host=h;Database=prod_cms;Username=u;Password=pw" };
        using var sp = Services(files, db);
        _storage.Description = new LocalFileStorage(files).Describe();
        _storage.Blobs[Key(1)] = Old;
        _storage.Blobs[Key(2)] = Old;
        Row(Key(2));

        foreach (var mode in new[] { Array.Empty<string>(), ["--apply"] })
        {
            var (o, e) = (new StringWriter(), new StringWriter());
            await MigrationCli.RunAsync(MigrationCli.Parse(["files:sweep", .. mode]), sp, o, e, default);
            var lines = o.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines[0].Should().Contain("storage:").And.Contain("local").And.Contain("uploads");
            lines[1].Should().Contain("database:").And.Contain("PostgreSQL").And.Contain("prod_cms");
        }
    }

    [Fact]
    public async Task S3_and_connection_secrets_never_reach_the_output()
    {
        var files = new FileStorageOptions
        {
            Backend = "s3",
            S3 =
            {
                Endpoint = "https://minio.example.test:9443/base", Bucket = "media-bucket",
                AccessKey = "AKIA-ACCESS-9", SecretKey = "S3-SECRET-7",
            },
        };
        var db = new DatabaseOptions
        {
            DbType = StruoDbType.PostgreSQL,
            ConnectionString = "Host=db.internal;Port=5432;Database=cms_db;Username=cms_user;Password=PG-PASSWORD-5",
        };
        using var sp = Services(files, db);
        using var s3 = new S3FileStorage(files);
        _storage.Description = s3.Describe();
        _storage.Blobs[Key(1)] = Old;

        var (o, e) = (new StringWriter(), new StringWriter());
        await MigrationCli.RunAsync(MigrationCli.Parse(["files:sweep", "--apply", "--force"]), sp, o, e, default);
        var all = o + e.ToString();

        all.Should().Contain("media-bucket").And.Contain("minio.example.test").And.Contain("cms_db");
        all.Should().NotContain("S3-SECRET-7").And.NotContain("AKIA-ACCESS-9")
            .And.NotContain("PG-PASSWORD-5").And.NotContain("cms_user").And.NotContain("db.internal");
    }

    [Fact]
    public async Task A_custom_storage_is_named_by_its_own_description()
    {
        _storage.Description = "Azure Blob container media";

        var (o, e) = (new StringWriter(), new StringWriter());
        await MigrationCli.RunAsync(MigrationCli.Parse(["files:sweep"]), _sp, o, e, default);

        o.ToString().Split('\n')[0].Should().Contain("storage:").And.Contain("Azure Blob container media");
    }

    [Fact]
    public async Task A_sqlite_database_is_named_by_its_file_only()
    {
        var db = new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = "Data Source=/secret/dir/app.db" };
        using var sp = Services(new FileStorageOptions(), db);

        var (o, e) = (new StringWriter(), new StringWriter());
        await MigrationCli.RunAsync(MigrationCli.Parse(["files:sweep"]), sp, o, e, default);

        o.ToString().Should().Contain("app.db").And.NotContain("/secret/dir");
    }

    [Theory]
    [InlineData("--min-age-hours")]
    [InlineData("--min-age-hours", "abc")]
    [InlineData("--min-age-hours", "-1")]
    [InlineData("--min-age-hours", "99999999")]
    [InlineData("--min-age-hours", "2147483647")]
    [InlineData("--bogus")]
    [InlineData("stray")]
    public async Task Bad_arguments_are_a_usage_error(params string[] args)
    {
        _storage.Blobs[Key(1)] = Old;

        var (code, _, err) = await Run(args);

        code.Should().Be(MigrationCli.Usage);
        err.Should().NotBeEmpty();
        _storage.Blobs.Should().ContainKey(Key(1));
    }

    [Fact]
    public void Files_sweep_is_a_command_and_keeps_its_arguments()
    {
        var inv = MigrationCli.Parse(["files:sweep", "--apply", "--min-age-hours", "6", "--Struo:Files:Backend=local"]);
        inv.Command.Should().Be("files:sweep");
        inv.CommandArgs.Should().Equal("--apply", "--min-age-hours", "6");
        inv.HostArgs.Should().Equal("--Struo:Files:Backend=local");
    }
}
