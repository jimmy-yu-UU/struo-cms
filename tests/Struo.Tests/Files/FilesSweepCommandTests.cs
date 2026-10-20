using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Files;
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

        public async IAsyncEnumerable<StoredBlob> ListAsync([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            foreach (var (key, modified) in Blobs.ToArray()) yield return new StoredBlob(key, modified);
        }

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
        _sp = new ServiceCollection()
            .AddSingleton<IFileStorage>(_storage)
            .AddScoped<ISqlSugarClient>(_ => SqlSugarClientFactory.Create(
                _database.Options(), new TestCurrentUserAccessor(Tester)))
            .BuildServiceProvider();
        MigrationCli.Clock = new FixedClock();
    }

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
        output.Should().Contain(Key(1)).And.Contain("1 orphan");
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
        output.Should().Contain("0 orphan").And.Contain("1 younger");
        _storage.Blobs.Should().ContainKey(Key(1));
    }

    [Fact]
    public async Task Min_age_hours_moves_the_grace_period()
    {
        _storage.Blobs[Key(1)] = Young;

        var (_, kept, _) = await Run("--min-age-hours=72", "--apply");
        kept.Should().Contain("0 orphan");
        _storage.Blobs.Should().ContainKey(Key(1));

        var (_, output, _) = await Run("--min-age-hours", "1");
        output.Should().Contain(Key(1));
    }

    [Fact]
    public async Task A_blob_of_a_trashed_row_is_not_an_orphan()
    {
        _storage.Blobs[Key(1)] = Old;
        Row(Key(1), trashed: true);

        var (_, output, _) = await Run("--apply");

        output.Should().Contain("0 orphan");
        _storage.Blobs.Should().ContainKey(Key(1));
    }

    [Fact]
    public async Task Keys_that_are_not_storage_keys_are_ignored()
    {
        var cacheKey = "image-cache/ab/" + new string('a', 64);
        _storage.Blobs[cacheKey] = Old;
        _storage.Blobs["2026/09/readme.txt"] = Old;
        _storage.Blobs[Key(3, "")] = Old;

        var (_, output, _) = await Run("--apply");

        output.Should().Contain("1 deleted");
        _storage.Blobs.Keys.Should().BeEquivalentTo([cacheKey, "2026/09/readme.txt"]);
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

        var (code, output, err) = await Run("--apply");

        code.Should().Be(MigrationCli.Failure);
        err.Should().Contain(Key(1)).And.Contain("storage offline");
        output.Should().Contain("1 deleted").And.Contain("1 failed");
        _storage.Blobs.Keys.Should().BeEquivalentTo([Key(1)]);
    }

    [Theory]
    [InlineData("--min-age-hours")]
    [InlineData("--min-age-hours", "abc")]
    [InlineData("--min-age-hours", "-1")]
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
