using Microsoft.Extensions.Logging;

namespace Struo.Infrastructure.Files;

/// <summary>
/// Removes cache files of the earlier flat layout: <c>&lt;root&gt;/&lt;2 hex&gt;/&lt;64 hex&gt;</c> where the
/// directory is the key's first two characters, and the staging files
/// <c>.&lt;64 hex&gt;.&lt;32 hex&gt;.tmp</c> in such a directory. Nothing else is touched, because the cache
/// root may be a directory that holds other things.
/// </summary>
internal static class LegacyVariantLayout
{
    private const int ShardLength = 2;

    /// <summary>Deletes the matching files and then the shard directories left empty; returns the file count.</summary>
    public static int Remove(string root, ILogger logger)
    {
        if (!Directory.Exists(root)) return 0;
        var removed = 0;
        foreach (var shard in SafeDirectories(root, logger).Where(d => VariantCacheLayout.IsLowerHex(d.Name, ShardLength)))
        {
            removed += RemoveFiles(shard, logger);
            TryRemoveIfEmpty(shard, logger);
        }
        return removed;
    }

    internal static bool IsLegacyFile(string shardName, string fileName)
    {
        if (VariantCacheLayout.IsVariantName(fileName)) return fileName.StartsWith(shardName, StringComparison.Ordinal);
        return VariantCacheLayout.IsTempName(fileName)
            && fileName.AsSpan(1).StartsWith(shardName, StringComparison.Ordinal);
    }

    private static int RemoveFiles(DirectoryInfo shard, ILogger logger)
    {
        var removed = 0;
        try
        {
            foreach (var file in shard.EnumerateFiles().Where(f => IsLegacyFile(shard.Name, f.Name)))
            {
                try
                {
                    file.Delete();
                    removed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Could not remove image cache file {Path}.", file.FullName);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not list image cache directory {Path}.", shard.FullName);
        }
        return removed;
    }

    private static void TryRemoveIfEmpty(DirectoryInfo shard, ILogger logger)
    {
        try
        {
            if (!shard.EnumerateFileSystemInfos().Any()) shard.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not remove image cache directory {Path}.", shard.FullName);
        }
    }

    private static IReadOnlyList<DirectoryInfo> SafeDirectories(string root, ILogger logger)
    {
        try
        {
            return new DirectoryInfo(root).EnumerateDirectories().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not list image cache root {Root}.", root);
            return [];
        }
    }
}
