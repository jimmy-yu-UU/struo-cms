// tests/Struo.Tests/Support/LoggedApiHost.cs
using System.Net.Http.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using Struo.Api.Auth;

namespace Struo.Tests.Support;

/// <summary>
/// A derived API host whose log events land in a <see cref="CapturingLogSink"/> of the test's own, so an
/// assertion about what a request logged never sees the events of another test on the shared host.
/// </summary>
public static class LoggedApiHost
{
    public static WebApplicationFactory<Program> WithSink(this WebApplicationFactory<Program> host, CapturingLogSink sink) =>
        host.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<ILogEventSink>(sink)));

    /// <summary>An admin client logged in against <paramref name="host"/>, seeded through the shared <paramref name="seeder"/>.</summary>
    public static async Task<HttpClient> AdminClientAsync(this WebApplicationFactory<Program> host, ApiFactory seeder)
    {
        await seeder.EnsureAdminSeededAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        return client;
    }

    /// <summary>Asserts that nothing at Error level or above was logged.</summary>
    public static void ShouldHaveLoggedNoErrors(this CapturingLogSink sink) =>
        sink.Events.Where(e => e.Level >= LogEventLevel.Error).Select(e => e.RenderMessage()).Should().BeEmpty();
}
