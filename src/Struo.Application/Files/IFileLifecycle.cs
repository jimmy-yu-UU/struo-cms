namespace Struo.Application.Files;

/// <summary>
/// The delete lifecycle of a stored file, shared by the media library and the generic items API /
/// GraphQL deletes of the <c>file</c> collection. Trash keeps the row, translations and blob for
/// restore; purge removes translations, the row, the brand-logo reference and the stored blob.
/// </summary>
public interface IFileLifecycle
{
    /// <summary>Trashes the file; false when it does not exist or is already trashed.</summary>
    Task<bool> TrashAsync(Guid id, CancellationToken ct = default);

    /// <summary>Permanently deletes the file (trashed or live); false when it does not exist.</summary>
    Task<bool> PurgeAsync(Guid id, CancellationToken ct = default);
}
