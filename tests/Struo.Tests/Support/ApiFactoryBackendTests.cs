using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Tests.Api;
using Xunit;

namespace Struo.Tests.Support;

[Collection("ApiIntegration")]
public class ApiFactoryBackendTests(ApiFactory factory)
{
    [Fact]
    public void ApiFactory_database_follows_the_selected_backend()
    {
        var options = factory.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

        AssertFollowsBackend(options);
    }

    [Fact]
    public void CorsApiFactory_database_follows_the_selected_backend()
    {
        using var cors = new CorsAndCookieTests.CorsApiFactory();

        AssertFollowsBackend(cors.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value);
    }

    private static void AssertFollowsBackend(DatabaseOptions options)
    {
        options.DbType.Should().Be(TestBackend.DbType);
        if (TestBackend.IsLive) return;

        options.ConnectionString.Should().StartWith("Data Source=").And.EndWith(".db");
        Path.GetDirectoryName(options.ConnectionString["Data Source=".Length..])
            .Should().Be(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
    }
}
