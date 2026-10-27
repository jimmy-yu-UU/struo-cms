// src/Struo.Application/Query/Write/FileDeleteHandler.cs
using Struo.Application.Files;

namespace Struo.Application.Query;

/// <summary>
/// Routes the <c>file</c> collection's trash and purge through <see cref="IFileLifecycle"/>, the single
/// path shared with the media library: the Restrict guard, SetNull and junction cleanup, the brand-logo
/// reference and the stored blob. A file id that is not a Guid names no file, so it reports not found.
/// </summary>
public sealed class FileDeleteHandler(IFileLifecycle lifecycle) : ICollectionDeleteHandler
{
    public bool AppliesTo(string collection) =>
        string.Equals(collection, FileCollection.Name, StringComparison.OrdinalIgnoreCase);

    public bool IsValidId(string id) => Guid.TryParse(id, out _);

    public async Task<bool> TrashAsync(string id, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var fileId)) return false;
        await lifecycle.TrashAsync(fileId, ct);
        return true; // an already-trashed file is idempotent success, like the generic trash path
    }

    public Task<bool> PurgeAsync(string id, CancellationToken ct) =>
        Guid.TryParse(id, out var fileId) ? lifecycle.PurgeAsync(fileId, ct) : Task.FromResult(false);
}
