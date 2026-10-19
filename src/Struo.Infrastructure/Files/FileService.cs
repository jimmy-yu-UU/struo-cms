using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Changes;
using Struo.Application.Files;
using Struo.Application.Localization;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Settings;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Orchestrates file uploads (validate → store bytes → extract image dimensions → insert a
/// <see cref="File"/> row with the configured default status) and file lookup/trash/restore/purge. Lives in Infrastructure so it can
/// reference the framework <see cref="File"/> entity directly. Bypasses <c>ItemService</c>, so each
/// write path raises its own post-commit <see cref="IItemChangeNotifier"/> notification.
/// </summary>
public sealed class FileService(
    ISqlSugarClient db,
    FileStorageServices storage,
    IItemRepository repository,
    ILanguageProvider languages,
    ICurrentUserAccessor currentUser,
    IItemChangeNotifier? notifier = null,
    ILogger<FileService>? logger = null,
    // Reference handling shared with every collection's delete (Restrict guard, SetNull, junctions,
    // translations). DI always supplies it; only upload-only test harnesses leave it null.
    ItemPurgePipeline? references = null) : IFileLifecycle
{
    private readonly ILogger log = logger ?? NullLogger<FileService>.Instance;
    private readonly IFileStorage blobs = storage.Storage;
    private readonly IImageDimensionReader imageReader = storage.Images;
    private readonly FileStorageOptions fileOptions = storage.Options;

    // Width of the `filename` and `title` string columns (core migration `AsString(255)`).
    private const int NameColumnWidth = 255;

    // U5b: post-commit, best-effort, CancellationToken.None (the write already committed).
    private Task NotifyAsync(Guid id, ItemChangeKind kind)
    {
        var set = new ItemChangeSet();
        set.Add(FileCollection.Name, id.ToString(), kind);
        return NotifyAsync(set);
    }

    private Task NotifyAsync(ItemChangeSet set) =>
        notifier is null ? Task.CompletedTask : notifier.NotifyAsync(set.ToList(), CancellationToken.None);

    private ItemPurgePipeline References =>
        references ?? throw new InvalidOperationException("FileService was built without reference handling.");

    // The brand-logo reference is cleared with an entity-typed SetColumns so the nullable column gets a
    // typed NULL (a plain NULL literal makes Postgres raise 42804). A no-op when the file is not the logo.
    private Task ClearLogoReferenceAsync(Guid id, CancellationToken ct) =>
        db.Updateable<SiteSettings>()
            .SetColumns(s => new SiteSettings { LogoFileId = null })
            .Where(s => s.LogoFileId == id)
            .ExecuteCommandAsync(ct);

    public async Task<File> UploadAsync(
        Stream content, string fileName, string contentType, long length, Guid? folderId = null,
        CancellationToken ct = default)
    {
        if (length <= 0) throw new QueryException("Empty file.");
        if (length > fileOptions.MaxUploadBytes)
            throw new QueryException($"File exceeds the maximum size of {fileOptions.MaxUploadBytes} bytes.");
        if (fileOptions.AllowedContentTypes.Length > 0 &&
            !fileOptions.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new QueryException($"Content type '{contentType}' is not allowed.");

        // Spool through a FileBufferingReadStream instead of an unconditional MemoryStream —
        // small uploads (<= 64KB) stay in memory, but anything larger spills to a temp file,
        // bounding memory so N concurrent large uploads cannot amplify it by up to MaxUploadBytes
        // each. Access below mixes sequential forward reads (which pull more from `content` on demand)
        // and backward seeks to already-read offsets; the header sniff + image probe seek freely, then
        // the stream is fully drained once (line ~55) before the save read so Length is the true total.
        //
        // `length` above is the client-DECLARED size (from Content-Length) — a client that
        // lies (declares small, streams large) would otherwise sail past that guard and spool
        // unbounded bytes to a temp file. `bufferLimit` caps what FileBufferingReadStream will ever
        // buffer/spill to MaxUploadBytes regardless of what the client claimed; once actual bytes
        // exceed it, the stream throws IOException("Buffer limit exceeded.") on the next read, which
        // is caught below and re-surfaced as a proper 413. tempFileDirectoryAccessor reproduces the
        // framework default (ASPNETCORE_TEMP env var, else the OS temp dir) since that default lives
        // on an internal type we cannot reference directly.
        await using var buffer = new FileBufferingReadStream(
            content,
            memoryThreshold: 64 * 1024,
            bufferLimit: fileOptions.MaxUploadBytes,
            tempFileDirectoryAccessor: () =>
                Environment.GetEnvironmentVariable("ASPNETCORE_TEMP") is { Length: > 0 } dir
                    ? dir
                    : Path.GetTempPath());

        try
        {
            // Conservative content sniff: if the client claims a type we have a signature for,
            // the leading bytes must match it — blocks e.g. a script stored as image/png. Unknown
            // types pass.
            var header = new byte[12];
            var read = await buffer.ReadAsync(header.AsMemory(0, header.Length), ct);
            if (!FileSignatureValidator.IsConsistent(header.AsSpan(0, read), contentType))
                throw new QueryException($"File contents do not match the declared content type '{contentType}'.");

            buffer.Position = 0;
            var dims = imageReader.TryRead(buffer, contentType);

            // FileBufferingReadStream.Length only reflects bytes buffered SO FAR, not the true total,
            // until the inner stream has been fully consumed — S3FileStorage's PutObjectRequest reads
            // Stream.Length to size the upload, so an under-drained buffer here would ship a truncated
            // Content-Length. Force a full drain (spilling to the temp file past the memory threshold,
            // same as a large upload always would) before rewinding for the actual save read.
            await buffer.CopyToAsync(Stream.Null, ct);
            buffer.Position = 0;

            return await SaveAsync(buffer, fileName, contentType, dims, folderId, ct);
        }
        catch (IOException ex) when (ex.Message.Contains("Buffer limit", StringComparison.OrdinalIgnoreCase))
        {
            throw new PayloadTooLargeException(
                $"File exceeds the maximum size of {fileOptions.MaxUploadBytes} bytes.");
        }
    }

    // Shortens the stem so the name fits the column and keeps its extension; an extension that
    // alone fills the column is cut with the rest of the name.
    internal static string ClampFileName(string fileName)
    {
        if (fileName.Length <= NameColumnWidth) return fileName;
        var ext = Path.GetExtension(fileName);
        if (ext.Length >= NameColumnWidth) return CutWhole(fileName, NameColumnWidth);
        return CutWhole(fileName, NameColumnWidth - ext.Length) + ext;
    }

    // Cuts to at most `length` UTF-16 units without leaving half of a surrogate pair.
    private static string CutWhole(string value, int length) =>
        char.IsHighSurrogate(value[length - 1]) ? value[..(length - 1)] : value[..length];

    private async Task<File> SaveAsync(
        Stream buffer, string fileName, string contentType, (int Width, int Height)? dims,
        Guid? folderId, CancellationToken ct)
    {
        if (folderId is { } fid)
        {
            // App-only existence check (accepted stance: no DB FK). A folder deleted between
            // this check and the insert is the same narrow race every FK-less write here has.
            var folder = await db.Queryable<MediaFolder>().In(fid).FirstAsync(ct);
            if (folder is null) throw new QueryException($"Folder '{fid}' does not exist.");
        }

        var key = StorageKey.Create(fileName);
        await blobs.SaveAsync(key, buffer, contentType, ct);

        var entity = new File
        {
            Id = Guid.NewGuid(),
            StorageKey = key,
            FileName = ClampFileName(fileName),
            ContentType = contentType,
            // `length` is the client-declared Content-Length, which a lying client can
            // understate; `buffer` has already been fully drained (see UploadAsync) so its Length
            // is the true byte count actually stored, and overflow past MaxUploadBytes has already
            // thrown by this point.
            Size = buffer.Length,
            Width = dims?.Width,
            Height = dims?.Height,
            Status = fileOptions.DefaultStatus,
            FolderId = folderId,
        };

        // Seed the default-locale Title from the filename (extension stripped) so an upload is
        // immediately human-readable everywhere. Dotfiles ("." prefix strips to empty) fall back to
        // the full name; clamp to the column width. Same transaction as the file row —
        // a seed failure must not leave a title-less file (repository join-if-active).
        var title = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(title)) title = fileName;
        if (title.Length > NameColumnWidth) title = CutWhole(title, NameColumnWidth);
        var translation = new FileTranslation
        {
            FileId = entity.Id, Locale = await languages.DefaultCodeAsync(), Title = title,
        };

        // ExecuteReturnEntityAsync has no CancellationToken overload (5.1.4.216); the File PK is a
        // client-generated Guid set above, so there is no DB-generated value to read back and
        // ExecuteCommandAsync(ct) + returning the same instance is equivalent while forwarding ct.
        await repository.InTransactionAsync(async () =>
        {
            await db.Insertable(entity).ExecuteCommandAsync(ct);
            await db.Insertable(translation).ExecuteCommandAsync(ct);
        }, ct);
        await NotifyAsync(entity.Id, ItemChangeKind.Created);
        return entity;
    }

    // InSingleAsync has no CancellationToken overload; In(id).FirstAsync(ct) forwards the token.
    public async Task<File?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Queryable<File>().In(id).FirstAsync(ct);

    // This is the media library's PURGE operation (permanent, hard delete) — the default
    // FilesController DELETE is TrashAsync above; DeleteAsync is invoked via ?purge=true and must
    // therefore still find an already-trashed row, so the lookup clears the ISoftDeletable filter
    // (Updateable/Deleteable below already bypass it; only this initial Queryable read needed it).
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.Queryable<File>().ClearFilter<Struo.Domain.Auditing.ISoftDeletable>().In(id).FirstAsync(ct);
        if (row is null) return false;

        // One transaction: the shared purge pipeline (Restrict guard, SetNull of referencing rows, junction
        // rows, translations, the row) plus the logo reference. Nesting-safe, so it joins an outer unit of work.
        var changes = new ItemChangeSet();
        var purged = await repository.InTransactionAsync(async () =>
        {
            var removed = await References.PurgeCoreAsync(
                FileCollection.Name, id.ToString(), new HashSet<(string Collection, string Id)>(), changes, ct);
            if (removed) await ClearLogoReferenceAsync(id, ct);
            return removed;
        }, ct);
        // A concurrent purge that won between the pre-read and the transaction leaves nothing to announce.
        if (!purged) return false;

        await NotifyAsync(changes);
        // The blob delete stays outside the transaction: best-effort (row gone, bytes orphaned).
        await BlobCleanup.TryDeleteAsync(blobs, row.StorageKey, log);
        return true;
    }

    public Task<bool> PurgeAsync(Guid id, CancellationToken ct = default) => DeleteAsync(id, ct);

    // Default "delete" for the media library is trash, not purge. Reuses the same atomic
    // repository primitive ItemService uses for every other soft-deletable collection (WHERE
    // deletedat IS NULL) instead of reimplementing that logic here.
    public async Task<bool> TrashAsync(Guid id, CancellationToken ct = default)
    {
        var trashed = await repository.InTransactionAsync(async () =>
        {
            await References.CheckRestrictAsync(FileCollection.Name, id.ToString(), ct);
            var trashedNow = await repository.SoftDeleteAsync(
                FileCollection.Name, id.ToString(), DateTime.UtcNow, currentUser.GetCurrentUserId(), ct);
            if (!trashedNow) return false;
            // A trashed file is filtered out of every read; a trashed brand logo would resolve into a dead
            // /content URL, so the reference is cleared with the trash.
            await ClearLogoReferenceAsync(id, ct);
            return true;
        }, ct);
        if (trashed) await NotifyAsync(id, ItemChangeKind.Trashed);
        return trashed;   // blob + FileTranslation rows retained for restore
    }

    public async Task<bool> RestoreAsync(Guid id, CancellationToken ct = default)
    {
        var restored = await repository.RestoreAsync(FileCollection.Name, id.ToString(), ct);
        if (restored) await NotifyAsync(id, ItemChangeKind.Restored);
        return restored;
    }
}
