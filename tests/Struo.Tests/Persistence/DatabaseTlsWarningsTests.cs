using AwesomeAssertions;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

/// <summary>
/// Production warns once when a non-local PostgreSQL, SQL Server or MySQL/MariaDB connection string
/// does not verify the server certificate. The warning names the setting and the safe value, never the
/// connection string, a user name or a password.
/// </summary>
public sealed class DatabaseTlsWarningsTests
{
    private const string Secret = "S3cretPw!";

    private static List<string> Run(StruoDbType type, string connectionString, string environment = "Production")
    {
        var logger = new ListLogger();
        DatabaseTlsWarnings.Log(type, connectionString, new HostingEnvironment { EnvironmentName = environment }, logger);
        logger.Entries.Should().OnlyContain(e => e.Level == LogLevel.Warning);
        return logger.Entries.Select(e => e.Message).ToList();
    }

    private static string Pg(string host, string tls = "") =>
        $"Host={host};Database=app;Username=app_user;Password={Secret};{tls}";

    private static string Mssql(string source, string tls = "") =>
        $"Data Source={source};Initial Catalog=app;User ID=app_user;Password={Secret};{tls}";

    private static string My(string server, string tls = "") =>
        $"Server={server};Database=app;User=app_user;Password={Secret};{tls}";

    private static string Insecure(StruoDbType type) => type switch
    {
        StruoDbType.PostgreSQL => Pg("db.internal"),
        StruoDbType.SqlServer => Mssql("db.internal", "Encrypt=False"),
        _ => My("db.internal"),
    };

    [Theory]
    [InlineData("")]
    [InlineData("SSL Mode=Require")]
    [InlineData("SSL Mode=Prefer")]
    [InlineData("SSL Mode=Disable")]
    public void PostgreSql_without_certificate_verification_warns(string tls) =>
        Run(StruoDbType.PostgreSQL, Pg("db.internal", tls)).Should().ContainSingle()
            .Which.Should().Contain("SSL Mode").And.Contain("VerifyFull").And.Contain("20");

    [Theory]
    [InlineData("SSL Mode=VerifyFull")]
    [InlineData("SSL Mode=VerifyCA")]
    public void PostgreSql_verifying_the_certificate_does_not_warn(string tls) =>
        Run(StruoDbType.PostgreSQL, Pg("db.internal", tls)).Should().BeEmpty();

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("/var/run/postgresql")]
    [InlineData("localhost,127.0.0.1")]
    [InlineData("localhost:5432,127.0.0.1:5433")]
    [InlineData("[::1]:5432")]
    public void PostgreSql_local_hosts_do_not_warn(string host) =>
        Run(StruoDbType.PostgreSQL, Pg(host)).Should().BeEmpty();

    [Fact]
    public void PostgreSql_with_one_remote_host_among_several_warns() =>
        Run(StruoDbType.PostgreSQL, Pg("localhost,db.internal")).Should().ContainSingle();

    [Theory]
    [InlineData("Encrypt=Optional")]
    [InlineData("Encrypt=False")]
    [InlineData("TrustServerCertificate=True")]
    public void SqlServer_without_certificate_verification_warns(string tls) =>
        Run(StruoDbType.SqlServer, Mssql("db.internal", tls)).Should().ContainSingle()
            .Which.Should().Contain("TrustServerCertificate").And.Contain("Encrypt");

    // The driver's defaults are Encrypt=Mandatory and TrustServerCertificate=false: verified.
    [Theory]
    [InlineData("")]
    [InlineData("Encrypt=True")]
    [InlineData("Encrypt=Mandatory;TrustServerCertificate=False")]
    [InlineData("Encrypt=Strict")]
    [InlineData("Encrypt=Strict;TrustServerCertificate=True")]
    public void SqlServer_verifying_the_certificate_does_not_warn(string tls) =>
        Run(StruoDbType.SqlServer, Mssql("db.internal", tls)).Should().BeEmpty();

    [Theory]
    [InlineData("localhost", "TrustServerCertificate=True")]
    [InlineData("(local)", "Encrypt=False")]
    [InlineData(".", "Encrypt=False")]
    [InlineData("127.0.0.1,1433", "Encrypt=False")]
    [InlineData("tcp:localhost,1433", "TrustServerCertificate=True")]
    [InlineData("tcp:127.0.0.1", "Encrypt=False")]
    [InlineData("np:\\\\.\\pipe\\sql\\query", "Encrypt=False")]
    [InlineData("[::1],1433", "Encrypt=False")]
    [InlineData("(localdb)\\MSSQLLocalDB", "Encrypt=False")]
    [InlineData(".\\SQLEXPRESS", "Encrypt=False")]
    [InlineData("lpc:localhost", "Encrypt=False")]
    [InlineData("\\\\.\\pipe\\sql\\query", "Encrypt=False")]
    [InlineData("np:\\\\localhost\\pipe\\sql\\query", "Encrypt=False")]
    [InlineData("np:.", "Encrypt=False")]
    public void SqlServer_local_sources_do_not_warn(string source, string tls) =>
        Run(StruoDbType.SqlServer, Mssql(source, tls)).Should().BeEmpty();

    [Theory]
    [InlineData("db.internal\\SQLEXPRESS")]
    [InlineData("tcp:db.internal\\inst,1433")]
    [InlineData("np:\\\\db.internal\\pipe\\sql\\query")]
    [InlineData("\\\\db.internal\\pipe\\sql\\query")]
    [InlineData("np:db.internal")]
    [InlineData("lpc:db.internal")]
    public void SqlServer_remote_sources_with_an_instance_or_pipe_still_warn(string source) =>
        Run(StruoDbType.SqlServer, Mssql(source, "Encrypt=False")).Should().ContainSingle();

    [Theory]
    [InlineData("")]
    [InlineData("SSL Mode=Required")]
    [InlineData("SSL Mode=Preferred")]
    [InlineData("SSL Mode=None")]
    public void MySql_without_certificate_verification_warns(string tls) =>
        Run(StruoDbType.MySql, My("db.internal", tls)).Should().ContainSingle()
            .Which.Should().Contain("SSL Mode").And.Contain("VerifyFull").And.Contain("20");

    [Theory]
    [InlineData("SSL Mode=VerifyFull")]
    [InlineData("SSL Mode=VerifyCA")]
    public void MySql_verifying_the_certificate_does_not_warn(string tls) =>
        Run(StruoDbType.MySql, My("db.internal", tls)).Should().BeEmpty();

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void MySql_local_servers_do_not_warn(string server) =>
        Run(StruoDbType.MySql, My(server)).Should().BeEmpty();

    [Fact]
    public void MySql_socket_path_without_a_protocol_does_not_warn() =>
        Run(StruoDbType.MySql, My("/var/run/mysqld/mysqld.sock")).Should().BeEmpty();

    [Fact]
    public void MySql_named_pipe_to_a_remote_server_warns() =>
        Run(StruoDbType.MySql, My("db.internal", "Protocol=pipe")).Should().ContainSingle();

    [Fact]
    public void MySql_named_pipe_on_the_local_machine_does_not_warn() =>
        Run(StruoDbType.MySql, My(".", "Protocol=pipe")).Should().BeEmpty();

    [Fact]
    public void MySql_over_a_unix_socket_does_not_warn() =>
        Run(StruoDbType.MySql, My("/var/run/mysqld/mysqld.sock", "Protocol=Unix")).Should().BeEmpty();

    [Theory]
    [InlineData(StruoDbType.PostgreSQL)]
    [InlineData(StruoDbType.SqlServer)]
    [InlineData(StruoDbType.MySql)]
    public void Other_environments_never_warn(StruoDbType type) =>
        Run(type, Insecure(type), "Development").Should().BeEmpty();

    [Theory]
    [InlineData(StruoDbType.PostgreSQL)]
    [InlineData(StruoDbType.SqlServer)]
    [InlineData(StruoDbType.MySql)]
    public void An_unparseable_connection_string_neither_warns_nor_throws(StruoDbType type)
    {
        var act = () => Run(type, "this is ;;; not=a=valid;string=");

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void A_connection_string_value_that_overflows_neither_warns_nor_throws()
    {
        var act = () => Run(StruoDbType.SqlServer, "Data Source=db.internal;Connect Timeout=99999999999");

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Theory]
    [InlineData(StruoDbType.Sqlite)]
    [InlineData(StruoDbType.Oracle)]
    public void Sqlite_and_Oracle_are_not_checked(StruoDbType type) =>
        Run(type, "Data Source=db.internal;Password=" + Secret).Should().BeEmpty();

    [Theory]
    [InlineData(StruoDbType.PostgreSQL)]
    [InlineData(StruoDbType.SqlServer)]
    [InlineData(StruoDbType.MySql)]
    public void The_warning_never_contains_credentials_or_the_host(StruoDbType type)
    {
        var message = Run(type, Insecure(type)).Should().ContainSingle().Subject;

        message.Should().NotContain(Secret).And.NotContain("app_user").And.NotContain("db.internal");
    }
}
