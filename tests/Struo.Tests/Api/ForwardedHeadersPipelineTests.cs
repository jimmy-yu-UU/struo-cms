using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>The forwarded client address reaches the per-IP login limiter, so the headers are applied
/// ahead of it in the real pipeline.</summary>
[Collection("ApiIntegration")]
public class ForwardedHeadersPipelineTests(ApiFactory factory)
{
    private sealed class SetRemoteAddress : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, n) =>
            {
                ctx.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");
                return n();
            });
            next(app);
        };
    }

    private HttpClient Client(bool forwardedHeaders) =>
        factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Login:Enabled"] = "true",
                ["RateLimiting:Login:PermitLimit"] = "1",
                ["RateLimiting:Login:WindowSeconds"] = "60",
                ["ForwardedHeaders:Enabled"] = forwardedHeaders ? "true" : "false",
                ["ForwardedHeaders:KnownProxies:0"] = "10.1.2.3",
            }));
            b.ConfigureServices(s => s.AddTransient<IStartupFilter, SetRemoteAddress>());
        }).CreateClient();

    private static async Task<HttpStatusCode> LoginFromAsync(HttpClient client, string clientIp)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody@struo.local", password = "wrong-password" }),
        };
        req.Headers.Add("X-Forwarded-For", clientIp);
        return (await client.SendAsync(req)).StatusCode;
    }

    [Fact]
    public async Task Enabled_partitions_the_login_limiter_by_the_forwarded_client_address()
    {
        var client = Client(forwardedHeaders: true);

        (await LoginFromAsync(client, "203.0.113.1")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginFromAsync(client, "203.0.113.2")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginFromAsync(client, "203.0.113.1")).Should().Be((HttpStatusCode)429);
    }

    [Fact]
    public async Task Disabled_shares_one_limiter_partition_for_every_forwarded_address()
    {
        var client = Client(forwardedHeaders: false);

        (await LoginFromAsync(client, "203.0.113.1")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginFromAsync(client, "203.0.113.2")).Should().Be((HttpStatusCode)429);
    }
}
