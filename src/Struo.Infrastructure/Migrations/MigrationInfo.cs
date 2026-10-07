namespace Struo.Infrastructure.Migrations;

/// <summary>Applied: recorded and known. Pending: known, not recorded. Orphaned: recorded, absent from the scanned assemblies.</summary>
public enum MigrationState { Applied, Pending, Orphaned }

public sealed record MigrationInfo(long Version, string Description, MigrationState State, DateTime? AppliedOn);
