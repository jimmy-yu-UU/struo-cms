using SqlSugar;

namespace Struo.Infrastructure.Migrations;

/// <summary>Read-only projection of the version table, queried with <c>.AS(tableName)</c>. It carries no <c>[SugarTable]</c>, so the entity scan never treats it as a framework table.</summary>
internal sealed class SchemaVersionRow
{
    [SugarColumn(ColumnName = "version")] public long Version { get; set; }
    [SugarColumn(ColumnName = "appliedon")] public DateTime? AppliedOn { get; set; }
    [SugarColumn(ColumnName = "description")] public string? Description { get; set; }
}
