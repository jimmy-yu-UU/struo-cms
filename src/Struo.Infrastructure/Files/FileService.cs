using Microsoft.AspNetCore.WebUtilities;
using SqlSugar;
using Struo.Application.Files;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Settings;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Orchestrates file uploads (validate → store bytes → extract image dimensions → insert a published
/// <see cref="File"/> row) and file lookup/delete. Lives in Infrastructure so it can reference the
/// framework <see cref="File"/> entity directly.
/// </summary>
public sealed class FileService(
    ISqlSugarClient db,
    IFileStorage storage,
    IImageDimensionReader images,
    FileStorageOptions options,
    IItemRepository repository)
{
    public async Task<File> UploadAsync(
        Stream content, string fileName, string contentType, long length, CancellationToken ct = default)
    {
        if (length <= 0) throw new QueryException("Empty file.");
        if (length > options.MaxUploadBytes)
            throw new QueryException($"File exceeds the maximum size of {options.MaxUploadBytes} bytes.");
        if (options.AllowedContentTypes.Length > 0 &&
            !options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new QueryException($"Content type '{contentType}' is not allowed.");

        // SEC-9: spool through a FileBufferingReadStream instead of an unconditional MemoryStream —
        // small uploads (<= 64KB) stay in memory exactly as before, but anything larger spills to a
        // temp file, so N concurrent large uploads no longer amplify memory by up to MaxUploadBytes
        // each. Access below mixes sequential forward reads (which pull more from `content` on demand)
        // and backward seeks to already-read offsets; the header sniff + image probe seek freely, then
        // the stream is fully drained once (line ~55) before the save read so Length is the true total.
        await using var buffer = new FileBufferingReadStream(content, memoryThreshold: 64 * 1024);

        // Conservative content sniff (L2): if the client claims a type we have a signature for, the
        // leading bytes must match it — blocks e.g. a script stored as image/png. Unknown types pass.
        var header = new byte[12];
        var read = await buffer.ReadAsync(header.AsMemory(0, header.Length), ct);
        if (!FileSignatureValidator.IsConsistent(header.AsSpan(0, read), contentType))
            throw new QueryException($"File contents do not match the declared content type '{contentType}'.");

        buffer.Position = 0;
        var dims = images.TryRead(buffer, contentType);

        // FileBufferingReadStream.Length only reflects bytes buffered SO FAR, not the true total,
        // until the inner stream has been fully consumed — S3FileStorage's PutObjectRequest reads
        // Stream.Length to size the upload, so an under-drained buffer here would ship a truncated
        // Content-Length. Force a full drain (spilling to the temp file past the memory threshold,
        // same as a large upload always would) before rewinding for the actual save read.
        await buffer.CopyToAsync(Stream.Null, ct);
        buffer.Position = 0;

        var key = StorageKey.Create(fileName);
        await storage.SaveAsync(key, buffer, contentType, ct);

        var entity = new File
        {
            Id = Guid.NewGuid(),
            StorageKey = key,
            FileName = fileName,
            ContentType = contentType,
            Size = length,
            Width = dims?.Width,
            Height = dims?.Height,
            Status = "published",
        };
        // ExecuteReturnEntityAsync has no CancellationToken overload (5.1.4.215); the File PK is a
        // client-generated Guid set above, so there is no DB-generated value to read back and
        // ExecuteCommandAsync(ct) + returning the same instance is equivalent while forwarding ct.
        await db.Insertable(entity).ExecuteCommandAsync(ct);
        return entity;
    }

    // InSingleAsync has no CancellationToken overload; In(id).FirstAsync(ct) forwards the token.
    public async Task<File?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Queryable<File>().In(id).FirstAsync(ct);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.Queryable<File>().In(id).FirstAsync(ct);
        if (row is null) return false;

        var fkCol = db.EntityMaintenance.GetDbColumnName(nameof(FileTranslation.FileId), typeof(FileTranslation));
        // CS-8: delete the sidecar translations + the file row through the nesting-safe repository
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

            // SEC-10/DB-15: SettingsController only validates the logo file is published at SAVE
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

        // storage.DeleteAsync stays outside the transaction: best-effort (row gone, bytes orphaned).
        try { await storage.DeleteAsync(row.StorageKey, ct); } catch { /* best-effort: row gone, bytes orphaned */ }
        return true;
    }
}
