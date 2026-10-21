using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Xunit;

namespace Struo.Tests.Files;

public class SweepTargetsTests
{
    private static string Describe(StruoDbType type, string connection) =>
        SweepTargets.Describe(new Struo.Application.Files.FileStorageOptions(),
            new DatabaseOptions { DbType = type, ConnectionString = connection });

    [Theory]
    [InlineData("Data Source=/var/data/site.db")]
    [InlineData("DataSource=/var/data/site.db")]
    [InlineData("Filename=/var/data/site.db")]
    [InlineData("filename=/var/data/site.db")]
    public void A_SQLite_file_is_named_under_any_of_its_connection_string_keys(string connection) =>
        Describe(StruoDbType.Sqlite, connection).Should().Contain("database site.db");

    [Fact]
    public void A_SQLite_string_without_a_file_prints_a_placeholder() =>
        Describe(StruoDbType.Sqlite, "Cache=Shared").Should().Contain("database ?");

    [Theory]
    [InlineData("Host=h;Database=appdb;Password=secret", "appdb")]
    [InlineData("Server=h;Initial Catalog=appdb;Password=secret", "appdb")]
    public void A_server_database_is_named_without_credentials(string connection, string name)
    {
        var text = Describe(StruoDbType.PostgreSQL, connection);

        text.Should().Contain($"database {name}").And.NotContain("secret");
    }

    [Fact]
    public void A_connection_string_naming_no_database_says_so() =>
        Describe(StruoDbType.PostgreSQL, "Host=h").Should().Contain("not named in the connection string");
}
