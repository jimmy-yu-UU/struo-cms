namespace Struo.Infrastructure.Migrations;

/// <summary>Applied: recorded and known. Pending: known, not recorded. Orphaned: recorded, absent from the scanned assemblies.</summary>
public enum MigrationState { Applied, Pending, Orphaned }

/// <summary>
/// One migration. <see cref="OutOfOrder"/> is true for a Pending migration whose version is lower than the
/// highest applied one: it is applied anyway, but a database built from scratch ran it in version order
/// and this one runs it last.
/// </summary>
public sealed record MigrationInfo(
    long Version, string Description, MigrationState State, DateTime? AppliedOn, bool OutOfOrder = false)
{
    /// <summary>The warning for the pending migrations in <paramref name="status"/> that are out of order, or null when there are none.</summary>
    public static string? OutOfOrderWarning(IReadOnlyList<MigrationInfo> status)
    {
        var outOfOrder = status.Where(m => m.OutOfOrder).Select(m => m.Version).ToList();
        if (outOfOrder.Count == 0) return null;

        var highest = status.Where(m => m.State != MigrationState.Pending).Max(m => m.Version);
        return $"Migration(s) {string.Join(", ", outOfOrder)} are out of order: older than the highest applied " +
               $"migration {highest}. They run after it here, while a new database runs them in version order, " +
               "so check that they do not depend on later migrations.";
    }
}
