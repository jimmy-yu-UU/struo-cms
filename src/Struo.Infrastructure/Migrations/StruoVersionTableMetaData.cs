using FluentMigrator.Runner.VersionTableInfo;

namespace Struo.Infrastructure.Migrations;

/// <summary>Version table <c>{prefix}schema_versions</c>. It has no primary key because the default PK index name collides on PostgreSQL.</summary>
public sealed class StruoVersionTableMetaData(string tablePrefix) : IVersionTableMetaData
{
    public static string TableNameFor(string prefix) => prefix + "schema_versions";

    public bool OwnsSchema => false;
    public string SchemaName => "";
    public string TableName => TableNameFor(tablePrefix);
    public string ColumnName => "version";
    public string DescriptionColumnName => "description";
    public string UniqueIndexName => "ux_" + TableName + "_version";
    public string AppliedOnColumnName => "appliedon";
    public bool CreateWithPrimaryKey => false;
}
