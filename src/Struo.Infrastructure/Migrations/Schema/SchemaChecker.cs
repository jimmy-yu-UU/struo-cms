using SqlSugar;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>
/// Compares the live database against the schema the entity types declare. Read-only: nothing is created
/// or altered. A fork test calls it with its own entity types and asserts <see cref="SchemaCheckReport.HasErrors"/>
/// is false.
/// </summary>
public static class SchemaChecker
{
    public static SchemaCheckReport Check(ISqlSugarClient db, StruoDbType dbType, IEnumerable<Type> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entityTypes);

        var expected = EntitySchemaReader.ReadAll(db, entityTypes);
        var liveTables = db.DbMaintenance.GetTableInfoList(false)
            .GroupBy(t => t.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Name);

        var findings = new List<SchemaFinding>();
        foreach (var table in expected)
        {
            if (!liveTables.TryGetValue(table.PhysicalName, out var liveName))
            {
                findings.Add(new SchemaFinding(FindingSeverity.Error, FindingKind.MissingTable, table.PhysicalName, null,
                    "table does not exist"));
                continue;
            }
            findings.AddRange(SchemaTableComparer.Compare(db, dbType, table, liveName));
        }
        findings.AddRange(SchemaTableComparer.UnsupportedIndexWarnings(dbType, expected));
        return new SchemaCheckReport(findings);
    }
}
