// tests/Struo.Tests/Support/ForkHost.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Serilog;
using Struo.Api.Hosting;

namespace Struo.Tests.Support;

/// <summary>
/// A host composed the way a fork's <c>Program.cs</c> does it (<c>AddStruoCms(configure)</c>,
/// <c>UseStruoCmsAsync()</c>) on a throwaway SQLite database and a TestServer, so a test can drive the
/// <see cref="StruoCmsBuilder"/> hooks and the startup checks through a real host. It resets the static
/// <c>Log.Logger</c>, as every <c>Program.cs</c> run does, so test classes that use it must belong to the
/// <c>ApiIntegration</c> collection to run serially with the other hosts. Every external target the
/// gitignored Development settings file may point at is overridden (<see cref="HostIsolation"/>).
/// </summary>
internal sealed class ForkHost : IAsyncDisposable
{
    private readonly ITestDatabase _db;
    private readonly string _filesRoot;

    private ForkHost(WebApplication app, ITestDatabase db, string filesRoot)
    {
        App = app;
        _db = db;
        _filesRoot = filesRoot;
    }

    public WebApplication App { get; }

    public HttpClient CreateClient() => App.GetTestClient();

    /// <summary>Starts a host; any startup failure propagates to the caller.</summary>
    public static async Task<ForkHost> StartAsync(
        Action<StruoCmsBuilder>? configure = null, Action<Dictionary<string, string?>>? mutate = null)
    {
        var db = TestBackend.CreateSqliteDatabase();
        var filesRoot = Path.Combine(Path.GetTempPath(), "struo-forkhost-" + Guid.NewGuid().ToString("N"));
        WebApplication? app = null;
        try
        {
            var settings = new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = db.ConnectionString,
                ["Database:MigrateOnStartup"] = "true",
            };
            foreach (var (key, value) in HostIsolation.ExternalTargets(filesRoot, Path.Combine(filesRoot, "image-cache")))
                settings[key] = value;
            mutate?.Invoke(settings);

            // Program.cs installs a fresh bootstrap logger before each host; a second UseSerilog on the same
            // one would find it frozen.
            Log.Logger = new LoggerConfiguration().CreateBootstrapLogger();
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(settings);
            builder.AddStruoCms(configure);
            app = builder.Build();
            await app.UseStruoCmsAsync();
            await app.StartAsync();
            return new ForkHost(app, db, filesRoot);
        }
        catch
        {
            if (app is not null) await app.DisposeAsync();
            Cleanup(db, filesRoot);
            throw;
        }
    }

    /// <summary>Starts a host and returns the failure it threw, or null when it started.</summary>
    public static async Task<Exception?> StartupErrorAsync(
        Action<StruoCmsBuilder>? configure = null, Action<Dictionary<string, string?>>? mutate = null)
    {
        try
        {
            await using var host = await StartAsync(configure, mutate);
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
        Cleanup(_db, _filesRoot);
    }

    private static void Cleanup(ITestDatabase db, string filesRoot)
    {
        db.Dispose();
        if (Directory.Exists(filesRoot)) Directory.Delete(filesRoot, recursive: true);
    }
}
