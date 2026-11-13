// tests/Struo.Tests/Files/FilePurgeTrashedReferenceTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Application.Files;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Localization;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Settings;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Files;

/// <summary>Soft-deletable source that points at a file through an OnDelete.Restrict relation.</summary>
[SugarTable("test_trashed_file_refs")]
[CmsCollection("TrashedFileRef")]
internal sealed class TrashedFileRef : AuditableEntity, ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    public Guid? FileId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(FileId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public File? Document { get; set; }
}

/// <summary>
/// The file purge (<c>FileService.DeleteAsync</c>) goes through the same purge pipeline as the generic
/// item purge: trashed rows that point at the file through a Restrict relation lose that key, live ones
/// block the purge.
/// </summary>
public sealed class FilePurgeTrashedReferenceTests : IDisposable
{
    private sealed class Storage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task DeleteAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public IAsyncEnumerable<StoredBlob> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public bool SupportsPresignedUrls => false;
        public Task<string?> GetPresignedUrlAsync(string key, TimeSpan ttl, CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private sealed class NoopImages : IImageDimensionReader
    {
        public (int Width, int Height)? TryRead(Stream seekable, string contentType) => null;
    }

    private sealed class StubLanguages : ILanguageProvider
    {
        public Task<IReadOnlyList<LanguageInfo>> GetEnabledAsync() => Task.FromResult<IReadOnlyList<LanguageInfo>>([]);
        public Task<string> DefaultCodeAsync() => Task.FromResult("en");
        public Task<bool> IsEnabledAsync(string code) => Task.FromResult(true);
        public Task<string?> CanonicalizeAsync(string code) => Task.FromResult<string?>(code);
        public void Invalidate() { }
    }

    private static readonly Guid Tester = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly FileService _svc;

    public FilePurgeTrashedReferenceTests()
    {
        _db = SqlSugarClientFactory.Create(_database.Options(), new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<File>();
        _db.CodeFirst.InitTables<FileTranslation>();
        _db.CodeFirst.InitTables<SiteSettings>();
        _db.CodeFirst.InitTables<MediaFolder>();
        _db.CodeFirst.InitTables<TrashedFileRef>();

        var types = new[] { typeof(File), typeof(MediaFolder), typeof(TrashedFileRef) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["file"] = typeof(File),
            ["mediafolder"] = typeof(MediaFolder),
            ["trashedFileRef"] = typeof(TrashedFileRef),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        _svc = new FileService(
            _db, new FileStorageServices(new Storage(), new NoopImages(), new FileStorageOptions()),
            new FileServiceCollaborators(repo, new StubLanguages(), new TestCurrentUserAccessor(Tester)),
            references: FileTestPipeline.Create(repo, provider, registry, graph));
    }

    public void Dispose()
    {
        _database.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task<Guid> InsertFileAsync()
    {
        var id = Guid.NewGuid();
        await _db.Insertable(new File
        {
            Id = id, StorageKey = "k", FileName = "a.png", ContentType = "image/png", Size = 1, Status = "published",
        }).ExecuteCommandAsync();
        return id;
    }

    private async Task<Guid> InsertRefAsync(Guid fileId, bool trashed)
    {
        var id = Guid.NewGuid();
        await _db.Insertable(new TrashedFileRef
        {
            Id = id, Name = "r", FileId = fileId, DeletedAt = trashed ? DateTime.UtcNow : null,
        }).ExecuteCommandAsync();
        return id;
    }

    private Task<TrashedFileRef> StoredRefAsync(Guid id) =>
        _db.Queryable<TrashedFileRef>().ClearFilter<ISoftDeletable>().In(id).FirstAsync();

    [Fact]
    public async Task Purging_a_file_clears_the_key_of_a_trashed_row_that_references_it()
    {
        var fileId = await InsertFileAsync();
        var refId = await InsertRefAsync(fileId, trashed: true);

        (await _svc.DeleteAsync(fileId)).Should().BeTrue();

        (await _svc.GetAsync(fileId)).Should().BeNull();
        var stored = await StoredRefAsync(refId);
        stored.FileId.Should().BeNull();
        stored.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task A_live_row_and_a_trashed_row_refuse_the_file_purge_and_keep_both_keys()
    {
        var fileId = await InsertFileAsync();
        var trashed = await InsertRefAsync(fileId, trashed: true);
        await InsertRefAsync(fileId, trashed: false);

        await Assert.ThrowsAsync<RelationConflictException>(() => _svc.DeleteAsync(fileId));

        (await _svc.GetAsync(fileId)).Should().NotBeNull();
        (await StoredRefAsync(trashed)).FileId.Should().Be(fileId);
    }
}
