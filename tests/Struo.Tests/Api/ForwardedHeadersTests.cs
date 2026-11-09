using System.Net;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Struo.Api.Http;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>The opt-in forwarded-headers wiring: honoured only when enabled and the connection's
/// source is a configured proxy/network; bad entries refuse to start.</summary>
public class ForwardedHeadersTests
{
    private static readonly IPAddress Client = IPAddress.Parse("10.1.2.3");

    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddStruoForwardedHeaders(builder.Configuration);
        var app = builder.Build();
        try
        {
            app.UseStruoForwardedHeaders();
            app.Run(ctx => ctx.Response.WriteAsync(
                $"{ctx.Request.Scheme}|{ctx.Connection.RemoteIpAddress}|{ctx.Request.Host}"));
            await app.StartAsync();
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static async Task<string> ObserveAsync(WebApplication app, IPAddress source)
    {
        var ctx = await app.GetTestServer().SendAsync(c =>
        {
            c.Connection.RemoteIpAddress = source;
            c.Request.Headers["X-Forwarded-Proto"] = "https";
            c.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
            c.Request.Headers["X-Forwarded-Host"] = "cms.example.com:8443";
            c.Request.Host = new Microsoft.AspNetCore.Http.HostString("internal");
        });
        using var reader = new StreamReader(ctx.Response.Body);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Disabled_ignores_forwarded_headers()
    {
        await using var app = await StartAsync(new() { ["ForwardedHeaders:Enabled"] = "false" });
        (await ObserveAsync(app, Client)).Should().Be("http|10.1.2.3|internal");
    }

    [Fact]
    public async Task Enabled_honours_headers_from_a_known_proxy()
    {
        await using var app = await StartAsync(new()
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:KnownProxies:0"] = "10.1.2.3",
        });
        (await ObserveAsync(app, Client)).Should().Be("https|203.0.113.9|cms.example.com:8443");
    }

    [Fact]
    public async Task Enabled_honours_headers_from_a_known_network()
    {
        await using var app = await StartAsync(new()
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:KnownNetworks:0"] = "10.0.0.0/8",
        });
        (await ObserveAsync(app, Client)).Should().Be("https|203.0.113.9|cms.example.com:8443");
    }

    [Fact]
    public async Task Enabled_ignores_headers_from_an_untrusted_source()
    {
        await using var app = await StartAsync(new()
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:KnownProxies:0"] = "10.9.9.9",
            ["ForwardedHeaders:KnownNetworks:0"] = "192.168.0.0/16",
        });
        (await ObserveAsync(app, Client)).Should().Be("http|10.1.2.3|internal");
    }

    [Theory]
    [InlineData("KnownProxies", "not-an-ip", "ForwardedHeaders:KnownProxies:0")]
    [InlineData("KnownNetworks", "10.0.0.0", "ForwardedHeaders:KnownNetworks:0")]
    [InlineData("KnownNetworks", "10.0.0.0/99", "ForwardedHeaders:KnownNetworks:0")]
    [InlineData("KnownNetworks", "bogus/8", "ForwardedHeaders:KnownNetworks:0")]
    public async Task Invalid_entry_refuses_to_start_naming_the_key(string list, string value, string key)
    {
        var act = () => StartAsync(new()
        {
            ["ForwardedHeaders:Enabled"] = "true",
            [$"ForwardedHeaders:{list}:0"] = value,
        });
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(key);
    }

    [Fact]
    public async Task Enabled_without_any_proxy_or_network_refuses_to_start()
    {
        var act = () => StartAsync(new() { ["ForwardedHeaders:Enabled"] = "true" });
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("ForwardedHeaders:KnownProxies").And.Contain("ForwardedHeaders:KnownNetworks");
    }

    [Fact]
    public async Task ForwardLimit_defaults_to_one_hop()
    {
        await using var app = await StartAsync(new()
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:KnownProxies:0"] = "10.1.2.3",
            ["ForwardedHeaders:KnownProxies:1"] = "10.1.2.4",
        });
        (await ObserveChainAsync(app, Client, "203.0.113.9, 10.1.2.4")).Should().Be("10.1.2.4");
    }

    [Fact]
    public async Task ForwardLimit_two_resolves_the_client_behind_two_known_proxies()
    {
        await using var app = await StartAsync(new()
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:ForwardLimit"] = "2",
            ["ForwardedHeaders:KnownProxies:0"] = "10.1.2.3",
            ["ForwardedHeaders:KnownProxies:1"] = "10.1.2.4",
        });
        (await ObserveChainAsync(app, Client, "203.0.113.9, 10.1.2.4")).Should().Be("203.0.113.9");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task ForwardLimit_below_one_refuses_to_start_naming_the_key(string value)
    {
        var act = () => StartAsync(new()
        {
            ["ForwardedHeaders:Enabled"] = "true",
            ["ForwardedHeaders:ForwardLimit"] = value,
            ["ForwardedHeaders:KnownProxies:0"] = "10.1.2.3",
        });
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("ForwardedHeaders:ForwardLimit");
    }

    private static async Task<string> ObserveChainAsync(WebApplication app, IPAddress source, string forwardedFor)
    {
        var ctx = await app.GetTestServer().SendAsync(c =>
        {
            c.Connection.RemoteIpAddress = source;
            c.Request.Headers["X-Forwarded-For"] = forwardedFor;
        });
        using var reader = new StreamReader(ctx.Response.Body);
        return (await reader.ReadToEndAsync()).Split('|')[1];
    }

    [Fact]
    public async Task Invalid_entries_are_ignored_while_disabled()
    {
        await using var app = await StartAsync(new()
        {
            ["ForwardedHeaders:Enabled"] = "false",
            ["ForwardedHeaders:KnownProxies:0"] = "not-an-ip",
        });
        (await ObserveAsync(app, Client)).Should().Be("http|10.1.2.3|internal");
    }
}
