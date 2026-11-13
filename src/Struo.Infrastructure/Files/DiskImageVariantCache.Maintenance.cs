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
    // purge or a sweep. A failure resets the running total, because a version may be half removed; a
    // concurrent removal of the same directory is not a failure.
    private void TidyFileDirectory(VariantCacheLayout.VariantKey written)
    {
        var fileDir = new DirectoryInfo(Path.Combine(rootPath, written.FileDir));
        try
        {
            var stale = fileDir.EnumerateDirectories()
                .Where(d => VariantCacheLayout.IsVersionDirName(d.Name) && d.Name != written.Version
                    && !VariantCacheLayout.IsReparsePoint(d))
                .ToList();
            foreach (var version in stale)
            {
                var freed = VariantsIn(version).Sum(f => f.Length);
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
            if (VariantCacheLayout.IsBenignRace(ex))
            {
                _logger.LogDebug(ex, "A concurrent change under {Path} ended an image variant tidy early.", fileDir.FullName);
                return;
            }
            ForgetTotal();
            _logger.LogWarning(ex, "Could not tidy the image variants under {Path}.", fileDir.FullName);
        }
    }

    private static IEnumerable<FileInfo> VariantsIn(DirectoryInfo versionDir) =>
        versionDir.EnumerateFiles()
            .Where(f => VariantCacheLayout.IsVariantName(f.Name) && !VariantCacheLayout.IsReparsePoint(f));

    private static IEnumerable<FileInfo> StaleTempsIn(DirectoryInfo versionDir, DateTime cutoffUtc) =>
        versionDir.EnumerateFiles()
            .Where(f => VariantCacheLayout.IsTempName(f.Name) && f.LastWriteTimeUtc < cutoffUtc
                && !VariantCacheLayout.IsReparsePoint(f));

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
            LogMaintenanceFailure(ex, rootPath);
        }
        return removed;
    }

    // Only <file id> and <file id>/<version> directories: never the root, never another shape, never a
    // reparse point.
    private int RemoveEmptyDirectories()
    {
        if (!Directory.Exists(rootPath)) return 0;
        var removed = 0;
        try
        {
            foreach (var fileDir in FileDirectories().ToList())
            {
                foreach (var version in fileDir.EnumerateDirectories()
                             .Where(d => VariantCacheLayout.IsVersionDirName(d.Name)
                                 && !VariantCacheLayout.IsReparsePoint(d)).ToList())
                {
                    if (TryRemoveIfEmpty(version)) removed++;
                }
                if (TryRemoveIfEmpty(fileDir)) removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogMaintenanceFailure(ex, rootPath);
        }
        return removed;
    }

    private IEnumerable<DirectoryInfo> FileDirectories() =>
        new DirectoryInfo(rootPath).EnumerateDirectories()
            .Where(d => VariantCacheLayout.IsFileDirName(d.Name) && !VariantCacheLayout.IsReparsePoint(d));

    private IEnumerable<DirectoryInfo> VersionDirectories() =>
        FileDirectories().SelectMany(d => d.EnumerateDirectories()
            .Where(v => VariantCacheLayout.IsVersionDirName(v.Name) && !VariantCacheLayout.IsReparsePoint(v)));

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
            LogMaintenanceFailure(ex, directory.FullName);
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
            LogMaintenanceFailure(ex, file.FullName);
            return false;
        }
    }

    // A directory that vanished or is no longer empty means another writer, purge or pass got there first.
    private void LogMaintenanceFailure(Exception ex, string path)
    {
        if (VariantCacheLayout.IsBenignRace(ex))
            _logger.LogDebug(ex, "A concurrent change interrupted image cache upkeep at {Path}.", path);
        else
            _logger.LogWarning(ex, "Image cache upkeep could not complete at {Path}.", path);
    }
}
