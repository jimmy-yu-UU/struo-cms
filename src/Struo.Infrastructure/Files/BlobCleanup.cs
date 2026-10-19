using Microsoft.Extensions.Logging;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

/// <summary>Best-effort blob removal after the owning row's delete has committed.</summary>
internal static class BlobCleanup
{
    /// <summary>
    /// Deletes the blob with <see cref="CancellationToken.None"/> (the row is already gone, so a
    /// disconnecting caller must not strand the bytes). A failure is logged as a Warning with the
    /// storage key and never thrown.
    /// </summary>
    public static async Task TryDeleteAsync(IFileStorage storage, string key, ILogger logger)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deleting the stored blob {StorageKey} failed; the bytes are orphaned.", key);
        }
    }
}
