namespace Struo.Infrastructure.Migrations;

/// <summary>
/// The statement about the bootstrap admin that a run reports when it applied the core seed: either the
/// admin it created or why none was created. It never carries the password or its hash.
/// </summary>
internal sealed record BootstrapAdminReport(bool Created, string Message)
{
    /// <summary>The report for the seed that <paramref name="applied"/> ran, or null when the run did not apply the seed.</summary>
    internal static BootstrapAdminReport? For(CoreSeedData? seed, IEnumerable<MigrationInfo> applied)
    {
        if (seed is null || !applied.Any(m => m.Version == Core.SeedCoreData.Version)) return null;
        return seed.HasAdmin
            ? new BootstrapAdminReport(true, $"Bootstrap admin created: {seed.AdminEmail}.")
            : new BootstrapAdminReport(false, NotCreatedMessage(seed.ContentRootPath));
    }

    private static string NotCreatedMessage(string? contentRootPath)
    {
        const string Head = "No bootstrap admin was created: Auth:BootstrapAdmin:Email is not set.";
        const string Switch = "--contentRoot <directory> reads the configuration files from another directory.";
        return contentRootPath is null
            ? $"{Head} {Switch}"
            : $"{Head} The configuration files were read from '{contentRootPath}'. {Switch}";
    }
}
