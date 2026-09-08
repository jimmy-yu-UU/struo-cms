using Microsoft.AspNetCore.WebUtilities;
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
/// Orchestrates file uploads (validate → store bytes → extract image dimensions → insert a published
/// <see cref="File"/> row) and file lookup/trash/restore/purge. Lives in Infrastructure so it can
/// reference the framework <see cref="File"/> entity directly. Bypasses <c>ItemService</c>, so each
/// write path raises its own post-commit <see cref="IItemChangeNotifier"/> notification.
/// </summary>
public sealed class FileService(
    ISqlSugarClient db,
    FileStorageServices storage,
    IItemRepository repository,
    ILanguageProvider languages,
    ICurrentUserAccessor currentUser,
    IItemChangeNotifier? notifier = null)
{
    private readonly IFileStorage blobs = storage.Storage;
    private readonly IImageDimensionReader imageReader = storage.Images;
    private readonly FileStorageOptions fileOptions = storage.Options;

    // U5b: post-commit, best-effort, CancellationToken.None (the write already committed).
    private Task NotifyAsync(Guid id, ItemChangeKind kind)
    {
        if (notifier is null) return Task.CompletedTask;
        var set = new ItemChangeSet();
        set.Add(FileCollection.Name, id.ToString(), kind);
        return notifier.NotifyAsync(set.ToList(), CancellationToken.None);
    }

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
        // small uploads (<= 64KB) stay in memory exactly as before, but anything larger spills to a
        // temp file, so N concurrent large uploads no longer amplify memory by up to MaxUploadBytes
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
            FileName = fileName,
            ContentType = contentType,
            // `length` is the client-declared Content-Length, which a lying client can
            // understate; `buffer` has already been fully drained (see UploadAsync) so its Length
            // is the true byte count actually stored, and overflow past MaxUploadBytes has already
            // thrown by this point.
            Size = buffer.Length,
            Width = dims?.Width,
            Height = dims?.Height,
            Status = "published",
            FolderId = folderId,
        };

        // Seed the default-locale Title from the filename (extension stripped) so an upload is
        // immediately human-readable everywhere. Dotfiles ("." prefix strips to empty) fall back to
        // the full name; clamp to the column width (varchar 255). Same transaction as the file row —
        // a seed failure must not leave a title-less file (repository join-if-active).
        var title = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrWhiteSpace(title)) title = fileName;
        if (title.Length > 255) title = title[..255];
        var translation = new FileTranslation
        {
            FileId = entity.Id, Locale = languages.DefaultCode(), Title = title,
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

        var fkCol = db.EntityMaintenance.GetDbColumnName(nameof(FileTranslation.FileId), typeof(FileTranslation));
        // Delete the sidecar translations + the file row through the nesting-safe repository
        // helper (join-if-active), so composing this inside a larger unit-of-work joins that outer
        // transaction instead of opening — and prematurely committing — its own inner one.
        await repository.InTransactionAsync(async () =>
        {
            await db.Deleteable<FileTranslation>()
                .Where(new List<IConditionalModel>
                {
                    new ConditionalModel
                    {
                        FieldName = fkCol, ConditionalType = ConditionalType.In, FieldValue = id.ToString()
                    }
                }).ExecuteCommandAsync(ct);
            await db.Deleteable<File>().In(id).ExecuteCommandAsync(ct);

            // SettingsController only validates the logo file is published at SAVE
            // time (TOCTOU) — if this deleted file was the current brand logo, clear the reference
            // now rather than leaving site_settings.logofileid dangling. Entity-typed SetColumns
            // (see SqlSugarSiteSettingsStore.UpdateRowAsync) so the nullable column gets a typed
            // NULL, avoiding the Postgres 42804 error a plain NULL literal triggers via SqlSugar's
            // untyped SetColumns overload. A no-op (0 rows affected) when this file was never the
            // logo, or when no site_settings row exists yet.
            await db.Updateable<SiteSettings>()
                .SetColumns(s => new SiteSettings { LogoFileId = null })
                .Where(s => s.LogoFileId == id)
                .ExecuteCommandAsync(ct);
        }, ct);

        await NotifyAsync(id, ItemChangeKind.Purged);
        // storage.DeleteAsync stays outside the transaction: best-effort (row gone, bytes orphaned).
        try { await blobs.DeleteAsync(row.StorageKey, ct); } catch { /* best-effort: row gone, bytes orphaned */ }
        return true;
    }

    // Default "delete" for the media library is trash, not purge. Reuses the same atomic
    // repository primitive ItemService uses for every other soft-deletable collection (WHERE
    // deletedat IS NULL) instead of reimplementing that logic here.
    public async Task<bool> TrashAsync(Guid id, CancellationToken ct = default)
    {
        var trashed = await repository.InTransactionAsync(async () =>
        {
            var trashedNow = await repository.SoftDeleteAsync(
                FileCollection.Name, id.ToString(), DateTime.UtcNow, currentUser.GetCurrentUserId(), ct);
            if (!trashedNow) return false;
            // A trashed file is filtered out of every read; if it is the current brand logo, clear the
            // reference now so ConfigController stops resolving it into a dead /content URL (mirrors
            // the logo-clear step in DeleteAsync/purge above).
            await db.Updateable<SiteSettings>()
                .SetColumns(s => new SiteSettings { LogoFileId = null })
                .Where(s => s.LogoFileId == id)
                .ExecuteCommandAsync(ct);
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
