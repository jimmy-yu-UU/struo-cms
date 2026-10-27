// tests/Struo.Tests/Support/ContentAssemblyEnvBootstrap.cs
using System.Runtime.CompilerServices;

namespace Struo.Tests.Support;

/// <summary>
/// Pins the configuration a test host must see while services are registered: the sample content
/// assembly, no Redis and no OIDC providers (below). Shipping the host without the Blog sample wired in removed the literal
/// <c>"Struo.Sample.Blog"</c> entry from <c>src/Struo.Api/appsettings.json</c>, so
/// <c>Struo:ContentAssemblies</c> is now empty by default. Every <c>WebApplicationFactory&lt;Program&gt;</c>
/// in this suite (<see cref="ApiFactory"/> and <c>CorsAndCookieTests.CorsApiFactory</c>) already layers a
/// <c>Struo:ContentAssemblies:0 = "Struo.Sample.Blog"</c> override via <c>ConfigureAppConfiguration</c> — but
/// that override arrives too late: <c>AddStruoCms</c> reads <c>builder.Configuration</c> to eagerly scan
/// content assemblies BEFORE <c>builder.Build()</c>, and <c>WebApplicationFactory</c>'s
/// <c>ConfigureAppConfiguration</c> hook is only spliced into the builder at the moment <c>Build()</c> is
/// intercepted (via <c>HostFactoryResolver</c>'s diagnostic listener) — i.e. strictly after that line already
/// ran. Confirmed empirically: instrumenting the host composition to print
/// <c>builder.Configuration.GetSection("Struo:ContentAssemblies")</c> right before the
/// <c>AddStruoMetadata</c> call showed <c>[]</c> even though the DI-resolved <c>IConfiguration</c> reads
/// back "Struo.Sample.Blog" once the host has finished starting.
///
/// Environment variables, by contrast, are folded into <c>WebApplicationBuilder.Configuration</c>
/// synchronously inside <c>WebApplication.CreateBuilder(args)</c> — before any of Program.cs's own code runs
/// — so they are visible to the eager scan. This module initializer sets the equivalent
/// <c>Struo__ContentAssemblies__0</c> environment variable once, before any test (and therefore before any
/// Program.cs invocation) runs, restoring exactly the same effective behavior the in-memory overrides were
/// already trying to express. It is process-wide, but every host built in this suite wants the sample
/// wired in, so there is no test that needs it absent while a real host is running.
/// </summary>
internal static class ContentAssemblyEnvBootstrap
{
    [ModuleInitializer]
    internal static void PinRegistrationTimeConfigurationForTestHosts()
    {
        Environment.SetEnvironmentVariable("Struo__ContentAssemblies__0", "Struo.Sample.Blog");

        // Redis and the OIDC providers are also read while services are registered, before a test host
        // can layer its own in-memory overrides, so the gitignored Development settings file would
        // otherwise connect the suite to the maintainer's Redis. A single space is "not configured" to
        // the Redis reader, and setting a variable to the empty string would delete it on Windows.
        Environment.SetEnvironmentVariable("Redis__ConnectionString", " ");
        Environment.SetEnvironmentVariable("Oidc__Providers__microsoft__Enabled", "false");
        Environment.SetEnvironmentVariable("Oidc__Providers__google__Enabled", "false");
    }
}
