using Microsoft.Extensions.Logging;

namespace Struo.Infrastructure.Files;

public sealed partial class DiskImageVariantCache
{
    /// <summary>
    /// Upkeep run at every start: removes files of the earlier flat layout, temp files left by writes that
    /// never finished, and empty directories of the cache layout. Never throws; failures are logged.
    /// </summary>
    public void RunMaintenance()
    {
        var legacy = LegacyVariantLayout.Remove(rootPath, _logger);
        var temps = RemoveStaleTemps();
        var directories = RemoveEmptyDirectories();
        if (legacy + temps + directories > 0)
            _logger.LogInformation(
                "Image variant cache upkeep removed {Legacy} file(s) of the earlier layout, "
                + "{Temps} stale temp file(s) and {Directories} empty director(ies) under {Root}.",
                legacy, temps, directories, rootPath);
    }

    // Other versions of the file are stale as soon as a newer one is written, and so are the temp files
    // of an interrupted write. Best-effort: what cannot be removed now is removed by a later write, a
    // purge or a sweep.
    private void TidyFileDirectory(VariantCacheLayout.VariantKey written)
    {
        var fileDir = new DirectoryInfo(Path.Combine(rootPath, written.FileDir));
        try
        {
            var stale = fileDir.EnumerateDirectories()
                .Where(d => VariantCacheLayout.IsVersionDirName(d.Name) && d.Name != written.Version)
                .ToList();
            foreach (var version in stale)
            {
                var freed = version.EnumerateFiles()
                    .Where(f => VariantCacheLayout.IsVariantName(f.Name)).Sum(f => f.Length);
                version.Delete(recursive: true);
                if (maxBytes > 0) AdjustTotal(-freed);
            }

            var current = new DirectoryInfo(VariantCacheLayout.VersionDirectory(rootPath, written));
            var cutoff = DateTime.UtcNow - VariantCacheLayout.StaleTempAge;
            foreach (var temp in StaleTempsIn(current, cutoff).ToList())
                TryDelete(temp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ForgetTotal();
            _logger.LogWarning(ex, "Could not tidy the image variants under {Path}.", fileDir.FullName);
        }
    }

    private static IEnumerable<FileInfo> StaleTempsIn(DirectoryInfo versionDir, DateTime cutoffUtc) =>
        versionDir.EnumerateFiles()
            .Where(f => VariantCacheLayout.IsTempName(f.Name) && f.LastWriteTimeUtc < cutoffUtc);

    private int RemoveStaleTemps()
    {
        if (!Directory.Exists(rootPath)) return 0;
        var removed = 0;
        var cutoff = DateTime.UtcNow - VariantCacheLayout.StaleTempAge;
        try
        {
            foreach (var temp in VersionDirectories().SelectMany(v => StaleTempsIn(v, cutoff)).ToList())
            {
                if (TryDelete(temp)) removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not scan image cache temp files under {Root}.", rootPath);
        }
        return removed;
    }

    // Only <file id> and <file id>/<version> directories: never the root, never another shape.
    private int RemoveEmptyDirectories()
    {
        if (!Directory.Exists(rootPath)) return 0;
        var removed = 0;
        try
        {
            foreach (var fileDir in new DirectoryInfo(rootPath).EnumerateDirectories()
                         .Where(d => VariantCacheLayout.IsFileDirName(d.Name)).ToList())
            {
                foreach (var version in fileDir.EnumerateDirectories()
                             .Where(d => VariantCacheLayout.IsVersionDirName(d.Name)).ToList())
                {
                    if (TryRemoveIfEmpty(version)) removed++;
                }
                if (TryRemoveIfEmpty(fileDir)) removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not scan image cache directories under {Root}.", rootPath);
        }
        return removed;
    }

    private IEnumerable<DirectoryInfo> VersionDirectories() =>
        new DirectoryInfo(rootPath).EnumerateDirectories()
            .Where(d => VariantCacheLayout.IsFileDirName(d.Name))
            .SelectMany(d => d.EnumerateDirectories()
                .Where(v => VariantCacheLayout.IsVersionDirName(v.Name)));

    private bool TryRemoveIfEmpty(DirectoryInfo directory)
    {
        try
        {
            if (directory.EnumerateFileSystemInfos().Any()) return false;
            directory.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove image cache directory {Path}.", directory.FullName);
            return false;
        }
    }

    private bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not remove image cache file {Path}.", file.FullName);
            return false;
        }
    }
}
