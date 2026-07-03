using SqlSugar;
using Struo.Application.Files;
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
    FileStorageOptions options)
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
        return (await db.Insertable(entity).ExecuteReturnEntityAsync())!;
    }

    public async Task<File?> GetAsync(Guid id, CancellationToken ct = default) =>
        await db.Queryable<File>().InSingleAsync(id);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.Queryable<File>().InSingleAsync(id);
        if (row is null) return false;

        var fkCol = db.EntityMaintenance.GetDbColumnName(nameof(FileTranslation.FileId), typeof(FileTranslation));
        try
        {
            await db.Ado.BeginTranAsync();
            await db.Deleteable<FileTranslation>()
                .Where(new List<IConditionalModel>
                {
                    new ConditionalModel
                    {
                        FieldName = fkCol, ConditionalType = ConditionalType.In, FieldValue = id.ToString()
                    }
                }).ExecuteCommandAsync(ct);
            await db.Deleteable<File>().In(id).ExecuteCommandAsync(ct);
            await db.Ado.CommitTranAsync();
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }

        try { await storage.DeleteAsync(row.StorageKey, ct); } catch { /* best-effort: row gone, bytes orphaned */ }
        return true;
    }
}
