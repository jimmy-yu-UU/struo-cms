using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Api;

/// <summary>A request body the server fails to read is a client error with a fixed message, not an internal error.</summary>
[Collection("ApiIntegration")]
public class RestBodyReadFailureTests(ApiFactory factory, ITestOutputHelper output)
{
    private const string Unreadable = "The request body could not be read.";

    private readonly CapturingLogSink _sink = new();

    public static TheoryData<string, string, bool, int> Cases()
    {
        var data = new TheoryData<string, string, bool, int>();
        foreach (var serverStatus in new[] { StatusCodes.Status408RequestTimeout, StatusCodes.Status400BadRequest })
        {
            data.Add("POST", "/api/items/fxArticle", true, serverStatus);
            data.Add("PUT", $"/api/items/fxArticle/{Guid.Empty}", true, serverStatus);
            data.Add("POST", "/api/auth/login", false, serverStatus);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task A_JSON_body_the_server_failed_to_read_is_a_BAD_USER_INPUT_answer_with_the_server_status(
        string method, string path, bool authenticated, int serverStatus)
    {
        var failure = new BadHttpRequestException("server detail", serverStatus);
        using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.AddSingleton<ILogEventSink>(_sink);
            s.AddSingleton<IStartupFilter>(new FailingBodyPathFilter(path, failure));
        }));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        if (authenticated)
        {
            await factory.EnsureAdminSeededAsync();
            (await client.PostAsJsonAsync("/api/auth/login",
                new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        }

        var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = new StringContent("{\"status\":\"draft\"}", Encoding.UTF8, "application/json"),
        };
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        var code = TryCode(body);
        var errorLog = _sink.Events.Any(e => e.Level >= LogEventLevel.Error);
        output.WriteLine($"MEASURE | S4 {method} {path} fails with {serverStatus} | {(int)response.StatusCode} | {code ?? "-"} | errorLog={errorLog} | {body}");

        ((int)response.StatusCode).Should().Be(serverStatus, body);
        var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Be(Unreadable);
        errorLog.Should().BeFalse();
    }

    private static string? TryCode(string body)
    {
        try
        {
            return JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("code").GetString();
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
