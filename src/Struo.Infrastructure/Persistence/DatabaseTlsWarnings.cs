using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Production startup warning for a PostgreSQL, SQL Server or MySQL/MariaDB connection string that talks
/// to a non-local server without verifying its certificate, which leaves the traffic open to a
/// man-in-the-middle. Only setting names and recommended values are logged, never the connection
/// string, host or credentials. An unparseable string is skipped: the connection itself reports it.
/// SQLite and Oracle are not checked.
/// </summary>
public static class DatabaseTlsWarnings
{
    public static void Log(StruoDbType dbType, string connectionString, IHostEnvironment env, ILogger logger)
    {
        if (!env.IsProduction() || string.IsNullOrWhiteSpace(connectionString)) return;

        var message = Check(dbType, connectionString);
        if (message is not null) logger.LogWarning("{Message}", message);
    }

    private static string? Check(StruoDbType dbType, string connectionString)
    {
        try
        {
            return dbType switch
            {
                StruoDbType.PostgreSQL => CheckPostgres(connectionString),
                StruoDbType.SqlServer => CheckSqlServer(connectionString),
                StruoDbType.MySql => CheckMySql(connectionString),
                _ => null,
            };
        }
        catch (Exception)
        {
            // Advice only: whatever a driver builder throws for a string it cannot read, startup goes on,
            // and the connection itself reports the string later.
            return null;
        }
    }

    private static string? CheckPostgres(string connectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (builder.SslMode is SslMode.VerifyCA or SslMode.VerifyFull) return null;
        if (AllLocal(SplitHosts(builder.Host ?? string.Empty), host => host.StartsWith('/') || IsLoopback(host))) return null;

        return Message("PostgreSQL", "SSL Mode is not VerifyCA or VerifyFull", "SSL Mode=VerifyFull");
    }

    private static string? CheckSqlServer(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var strict = builder.Encrypt == SqlConnectionEncryptOption.Strict;
        var verified = strict || (builder.Encrypt == SqlConnectionEncryptOption.Mandatory && !builder.TrustServerCertificate);
        if (verified || IsLocalSqlServer(builder.DataSource)) return null;

        return Message("SQL Server", "Encrypt is not Mandatory or Strict, or TrustServerCertificate is true",
            "Encrypt=True and TrustServerCertificate=False (or Encrypt=Strict)");
    }

    private static string? CheckMySql(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        if (builder.SslMode is MySqlSslMode.VerifyCA or MySqlSslMode.VerifyFull) return null;
        if (builder.ConnectionProtocol is MySqlConnectionProtocol.Unix or MySqlConnectionProtocol.UnixSocket
            or MySqlConnectionProtocol.SharedMemory or MySqlConnectionProtocol.Memory) return null;
        var servers = SplitHosts(builder.Server);
        // A named pipe is opened on the Server host, so it is local only when that host is.
        var isPipe = builder.ConnectionProtocol is MySqlConnectionProtocol.NamedPipe or MySqlConnectionProtocol.Pipe;
        if (AllLocal(servers, server => IsLocalMySqlServer(server, isPipe))) return null;

        return Message("MySQL or MariaDB", "SSL Mode is not VerifyCA or VerifyFull", "SSL Mode=VerifyFull");
    }

    private static bool IsLocalMySqlServer(string server, bool isPipe) =>
        server.StartsWith('/') || IsLoopback(server) || (isPipe && server == ".");

    private static string Message(string backend, string condition, string recommended) =>
        $"Database:ConnectionString connects to a non-local {backend} server without verifying its " +
        $"certificate ({condition}), so a man-in-the-middle can intercept the traffic. Set {recommended}. " +
        "See chapter 20 of the guide.";

    private static string[] SplitHosts(string hosts) =>
        hosts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    // An empty host list is not a remote server: the connection fails on its own.
    private static bool AllLocal(string[] hosts, Func<string, bool> isLocal) => hosts.All(isLocal);

    private static bool IsLoopback(string host)
    {
        var name = StripPort(host.Trim());
        return name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || name == "127.0.0.1" || name == "::1";
    }

    // "[::1]:5432" and "localhost:5432" lose the port; a bare IPv6 address such as "::1" has several
    // colons and is left alone.
    private static string StripPort(string host)
    {
        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');
            return close > 0 ? host[1..close] : host;
        }

        var colon = host.IndexOf(':');
        return colon >= 0 && colon == host.LastIndexOf(':') ? host[..colon] : host;
    }

    // The server named by a Data Source. A pipe or lpc source is local only when its host is: a pipe to
    // another machine travels over the network.
    private static bool IsLocalSqlServer(string dataSource)
    {
        var source = dataSource.Trim();
        foreach (var prefix in new[] { "tcp:", "np:", "lpc:" }.Where(p => source.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            source = source[prefix.Length..].TrimStart();
        }

        // A pipe source starts with two backslashes; its host is the segment before the next one.
        if (source.StartsWith(@"\\", StringComparison.Ordinal)) source = source[2..];

        var comma = source.LastIndexOf(',');
        if (comma >= 0) source = source[..comma];
        var instance = source.IndexOf('\\');
        if (instance >= 0) source = source[..instance];
        source = source.Trim();

        return source == "." || source.Equals("(local)", StringComparison.OrdinalIgnoreCase) || IsLoopback(source)
            || source.Equals("(localdb)", StringComparison.OrdinalIgnoreCase);
    }
}
