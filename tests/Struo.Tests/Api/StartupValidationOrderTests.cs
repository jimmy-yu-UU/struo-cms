using AwesomeAssertions;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Configuration is validated before the startup migration touches the database: a host with an invalid
/// option value and <c>Database:MigrateOnStartup=true</c> refuses to start and leaves the database empty.
/// </summary>
[Collection("ApiIntegration")]
public sealed class StartupValidationOrderTests
{
    private static IEnumerable<string> FailuresOf(Exception? error) => error switch
    {
        null => [],
        OptionsValidationException invalid => invalid.Failures,
        AggregateException aggregate => aggregate.InnerExceptions.SelectMany(FailuresOf),
        _ => FailuresOf(error.InnerException),
    };

    [SqliteOnlyTheory("options validation order; a fresh SQLite file shows whether the migration ran")]
    [InlineData("RateLimiting:Login:PermitLimit", "0", "RateLimiting:Login:PermitLimit")]
    [InlineData("RateLimiting:LoginConcurrency:PermitLimit", "0", "RateLimiting:LoginConcurrency:PermitLimit")]
    [InlineData("Query:MaxLimit", "0", "Query:MaxLimit")]
    [InlineData("Api:MaxJsonBodyBytes", "0", "Api:MaxJsonBodyBytes")]
    [InlineData("Auth:Password:MinLength", "0", "Auth:Password:MinLength")]
    [InlineData("Auth:Cookie:Name", "bad name", "Auth:Cookie:Name")]
    public async Task An_invalid_option_fails_startup_before_the_migration_runs(string key, string value, string named)
    {
        using var db = TestBackend.CreateSqliteDatabase();

        var error = await ForkHost.StartupErrorAsync(mutate: settings =>
        {
            settings["Database:ConnectionString"] = db.ConnectionString;
            settings[key] = value;
        });

        string.Join(" ", FailuresOf(error)).Should().Contain(named);
        using var client = new SqlSugarClient(new ConnectionConfig
        {
            DbType = SqlSugar.DbType.Sqlite, ConnectionString = db.ConnectionString, IsAutoCloseConnection = true,
        });
        client.DbMaintenance.GetTableInfoList(false).Should().BeEmpty("the migration must not run first");
    }
}
