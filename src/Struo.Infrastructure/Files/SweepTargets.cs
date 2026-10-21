using System.Data.Common;
using Struo.Application.Configuration;
using Struo.Application.Files;

namespace Struo.Infrastructure.Files;

/// <summary>Describes what <c>files:sweep</c> is about to compare, without credentials or connection strings.</summary>
internal static class SweepTargets
{
    public static string Describe(FileStorageOptions files, DatabaseOptions database) =>
        $"storage:  {DescribeStorage(files)}{Environment.NewLine}database: {DescribeDatabase(database)}";

    private static string DescribeStorage(FileStorageOptions files)
    {
        if (!string.Equals(files.Backend, "s3", StringComparison.OrdinalIgnoreCase))
            return $"local, root {Path.GetFullPath(files.Local.RootPath)}";

        var host = Uri.TryCreate(files.S3.Endpoint, UriKind.Absolute, out var uri)
            ? (uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}")
            : $"AWS region {files.S3.Region}";
        return $"s3, bucket {files.S3.Bucket}, service {host}";
    }

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
                return (isSqlite ? Path.GetFileName(value) : value) is { Length: > 0 } name ? name : "?";
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
