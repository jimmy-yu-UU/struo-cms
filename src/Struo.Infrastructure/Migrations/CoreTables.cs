using SqlSugar;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations;

/// <summary>Detects whether a database already holds the framework tables.</summary>
internal static class CoreTables
{
    /// <summary>True when the <c>users</c> table exists under <paramref name="tablePrefix"/>.</summary>
    public static bool Exist(ISqlSugarClient db, string tablePrefix) =>
        db.DbMaintenance.IsAnyTable(TableNaming.Apply(tablePrefix, "users"), false);
}
