using SqlSugar;
using Struo.Application.Files;
using Struo.Application.Query;
using Struo.Domain.Query;

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

        // Buffer once so we can read dimensions AND persist from the same bytes.
        await using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        // Conservative content sniff (L2): if the client claims a type we have a signature for, the
        // leading bytes must match it — blocks e.g. a script stored as image/png. Unknown types pass.
        buffer.Position = 0;
        var header = new byte[12];
        var read = buffer.Read(header, 0, header.Length);
        if (!FileSignatureValidator.IsConsistent(header.AsSpan(0, read), contentType))
            throw new QueryException($"File contents do not match the declared content type '{contentType}'.");

        buffer.Position = 0;
        var dims = images.TryRead(buffer, contentType);
        buffer.Position = 0;

        var key = StorageKey.Create(fileName);
        await storage.SaveAsync(key, buffer, ct);

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
        }, ct);

        // storage.DeleteAsync stays outside the transaction: best-effort (row gone, bytes orphaned).
        try { await storage.DeleteAsync(row.StorageKey, ct); } catch { /* best-effort: row gone, bytes orphaned */ }
        return true;
    }
}
