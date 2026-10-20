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

    private static string DatabaseName(DatabaseOptions database)
    {
        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = database.ConnectionString };
            if (database.DbType == StruoDbType.Sqlite)
                return builder.TryGetValue("Data Source", out var file) ? Path.GetFileName(file.ToString()) ?? "?" : "?";
            foreach (var key in new[] { "Database", "Initial Catalog" })
                if (builder.TryGetValue(key, out var name)) return name.ToString() ?? "?";
        }
        catch (ArgumentException)
        {
            // A malformed connection string has no readable name; the sweep itself reports the real failure.
        }
        return "(not named in the connection string)";
    }
}
