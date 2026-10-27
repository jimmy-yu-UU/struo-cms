using System.Data.Common;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Files;

/// <summary>Describes what <c>files:sweep</c> is about to compare, without credentials or connection strings.</summary>
internal static class SweepTargets
{
    public static string Describe(string storage, DatabaseOptions database) =>
        $"storage:  {storage}{Environment.NewLine}database: {DescribeDatabase(database)}";

    private static string DescribeDatabase(DatabaseOptions database) =>
        $"{database.DbType}, database {DatabaseName(database)}";

    private static readonly string[] SqliteFileKeys = ["Data Source", "DataSource", "Filename"];
    private static readonly string[] DatabaseNameKeys = ["Database", "Initial Catalog"];

    private static string DatabaseName(DatabaseOptions database)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = database.ConnectionString };
            var isSqlite = database.DbType == StruoDbType.Sqlite;
            var key = (isSqlite ? SqliteFileKeys : DatabaseNameKeys).FirstOrDefault(builder.ContainsKey);
            if (key is not null)
            {
                var value = builder[key].ToString();
                var name = isSqlite ? Path.GetFileName(value) : value;
                return string.IsNullOrEmpty(name) ? "?" : name;
            }
            if (isSqlite) return "?";
        }
        catch (ArgumentException)
        {
            // A malformed connection string has no readable name; the sweep itself reports the real failure.
        }
        return "(not named in the connection string)";
    }
}
