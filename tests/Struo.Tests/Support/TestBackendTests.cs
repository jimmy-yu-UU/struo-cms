using AwesomeAssertions;
using Struo.Application.Configuration;
using Xunit;

namespace Struo.Tests.Support;

public sealed class TestBackendTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_defaults_to_Sqlite_when_unset(string? value) =>
        TestBackend.Parse(value).Should().Be(TestBackendKind.Sqlite);

    [Theory]
    [InlineData("sqlserver", TestBackendKind.SqlServer)]
    [InlineData("SqlServer", TestBackendKind.SqlServer)]
    [InlineData("POSTGRESQL", TestBackendKind.PostgreSQL)]
    [InlineData("mysql", TestBackendKind.MySql)]
    [InlineData("MariaDb", TestBackendKind.MariaDb)]
    [InlineData("Sqlite", TestBackendKind.Sqlite)]
    public void Parse_is_case_insensitive_on_the_exact_names(string value, TestBackendKind expected) =>
        TestBackend.Parse(value).Should().Be(expected);

    [Theory]
    [InlineData("postgres")]
    [InlineData("3")]
    [InlineData("oracle")]
    public void Parse_rejects_unknown_values_and_lists_the_valid_ones(string value)
    {
        var act = () => TestBackend.Parse(value);

        var message = act.Should().Throw<InvalidOperationException>().Which.Message;
        message.Should().Contain("STRUO_TEST_BACKEND").And.Contain(value);
        foreach (var name in new[] { "Sqlite", "PostgreSQL", "SqlServer", "MySql", "MariaDb" })
            message.Should().Contain(name);
    }

    [Fact]
    public void DatabaseNameFor_builds_a_lowercase_name_that_keeps_the_run_id()
    {
        TestBackend.DatabaseNameFor("web-struo-cms-test-db", "api", "a1b2c3d4")
            .Should().Be("web_struo_cms_test_db_api_a1b2c3d4");
    }

    [Fact]
    public void DatabaseNameFor_truncates_the_configured_part_never_the_run_id()
    {
        var name = TestBackend.DatabaseNameFor(new string('x', 100) + "-test", "api", "a1b2c3d4");

        name.Length.Should().BeLessThanOrEqualTo(63);
        name.Should().EndWith("_a1b2c3d4");
        name.Should().MatchRegex("^[a-z0-9_]+$");
    }

    [Fact]
    public void DatabaseNameFor_always_contains_test()
    {
        TestBackend.DatabaseNameFor("Struo CMS", "Api Host", "a1b2c3d4")
            .Should().Contain("test").And.MatchRegex("^[a-z0-9_]+$").And.NotContain("__");
    }

    [Theory]
    [InlineData("web-struo-cms-test-db", "api")]
    [InlineData("struo-cms-test", "cors")]
    [InlineData("struo-cms-test", "a-very-long-purpose-name-that-pushes-the-name-over-the-limit-xx")]
    public void IsLeftoverName_matches_names_the_mechanism_produces(string configured, string purpose)
    {
        var name = TestBackend.DatabaseNameFor(configured, purpose, "0123abcd");

        TestBackend.IsLeftoverName(configured, name).Should().BeTrue();
    }

    [Theory]
    [InlineData("struo-cms-test")]
    [InlineData("struo_cms_test")]
    [InlineData("struo_cms_test_api")]
    [InlineData("struo_cms_test_api_0123ABCD")]
    [InlineData("struo_cms_test_api_0123abc")]
    [InlineData("other_test_api_0123abcd")]
    [InlineData("master")]
    public void IsLeftoverName_never_matches_the_configured_database_or_foreign_names(string name) =>
        TestBackend.IsLeftoverName("struo-cms-test", name).Should().BeFalse();

    [Theory]
    [InlineData(TestBackendKind.PostgreSQL, "STRUO_TEST_PG_CONNECTION", "Testing:PostgresConnection")]
    [InlineData(TestBackendKind.SqlServer, "STRUO_TEST_SQLSERVER_CONNECTION", "Testing:SqlServerConnection")]
    [InlineData(TestBackendKind.MySql, "STRUO_TEST_MYSQL_CONNECTION", "Testing:MySqlConnection")]
    [InlineData(TestBackendKind.MariaDb, "STRUO_TEST_MARIADB_CONNECTION", "Testing:MariaDbConnection")]
    public void RequireConnection_names_the_env_var_and_config_key_when_missing(
        TestBackendKind kind, string envVar, string configKey)
    {
        var act = () => TestBackend.RequireConnection(kind, null);

        act.Should().Throw<InvalidOperationException>().Which.Message
            .Should().Contain("STRUO_TEST_BACKEND").And.Contain(envVar).And.Contain(configKey);
    }

    [Fact]
    public void RequireConnection_returns_a_configured_connection() =>
        TestBackend.RequireConnection(TestBackendKind.MySql, "Server=x;Database=t-test")
            .Should().Be("Server=x;Database=t-test");

    [Fact]
    public void CreateLiveDatabase_rejects_Sqlite()
    {
        var act = () => TestBackend.CreateLiveDatabase(TestBackendKind.Sqlite, "api");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("PostgreSQL", TestBackendKind.PostgreSQL, StruoDbType.PostgreSQL)]
    [InlineData("SqlServer", TestBackendKind.SqlServer, StruoDbType.SqlServer)]
    [InlineData("MySql", TestBackendKind.MySql, StruoDbType.MySql)]
    [InlineData("MariaDb", TestBackendKind.MariaDb, StruoDbType.MySql)]
    public void Kinds_map_to_live_backends_and_db_types(string backend, TestBackendKind kind, StruoDbType db)
    {
        TestBackend.KindOf(LiveBackend.Get(backend)).Should().Be(kind);
        TestBackend.DbTypeOf(kind).Should().Be(db);
    }

    [Fact]
    public void Sqlite_databases_are_temp_files_removed_on_dispose()
    {
        var db = TestBackend.CreateSqliteDatabase();
        db.DbType.Should().Be(StruoDbType.Sqlite);
        var path = db.ConnectionString["Data Source=".Length..];
        File.WriteAllText(path, "x");

        db.Dispose();

        File.Exists(path).Should().BeFalse();
    }
}

public sealed class SqliteOnlyAttributeTests
{
    [Fact]
    public void Fact_variant_is_skipped_only_when_a_live_backend_is_selected()
    {
        var attribute = new SqliteOnlyFactAttribute("uses a file path");

        attribute.Skip.Should().Be(TestBackend.IsLive ? "SQLite-only: uses a file path" : null);
    }

    [Fact]
    public void Theory_variant_is_skipped_only_when_a_live_backend_is_selected()
    {
        var attribute = new SqliteOnlyTheoryAttribute("uses a file path");

        attribute.Skip.Should().Be(TestBackend.IsLive ? "SQLite-only: uses a file path" : null);
    }
}
