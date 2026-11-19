namespace Struo.Tests.Support;

/// <summary>
/// Pins the configuration a test host must see while services are registered. <c>AddStruoCms</c> reads
/// <c>Struo:ContentAssemblies</c>, Redis, the OIDC providers and the GraphQL limits from
/// <c>builder.Configuration</c> before <c>builder.Build()</c>, which is earlier than
/// <c>WebApplicationFactory</c> splices its <c>ConfigureAppConfiguration</c> overrides in. Environment
/// variables are folded into the builder during <c>WebApplication.CreateBuilder</c>, so they are the only
/// override that arrives in time.
/// </summary>
public static class TestHostEnvironment
{
    /// <summary>
    /// The directory the test process uses as <c>APPDATA</c>. It does not exist, so the path the framework
    /// derives for the developer's user-secrets file points at nothing.
    /// </summary>
    public static string EmptyAppDataRoot { get; } =
        Path.Combine(Path.GetTempPath(), "struo-test-appdata-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Sets the content assemblies every host in the process loads, and turns Redis and the two
    /// shipped OIDC providers (<c>microsoft</c>, <c>google</c>) off so a developer's gitignored
    /// Development settings cannot reach the suite. A provider a fork adds is not touched. It also points
    /// <c>APPDATA</c> at <see cref="EmptyAppDataRoot"/>, so the user-secrets file the framework looks for
    /// is never the developer's. Call it from a <c>[ModuleInitializer]</c> in each test project.
    /// </summary>
    public static void Pin(params string[] contentAssemblies)
    {
        for (var i = 0; i < contentAssemblies.Length; i++)
            Environment.SetEnvironmentVariable($"Struo__ContentAssemblies__{i}", contentAssemblies[i]);

        // A single space is "not configured" to the Redis reader; setting a variable to the empty
        // string would delete it on Windows.
        Environment.SetEnvironmentVariable("Redis__ConnectionString", " ");
        Environment.SetEnvironmentVariable("Oidc__Providers__microsoft__Enabled", "false");
        Environment.SetEnvironmentVariable("Oidc__Providers__google__Enabled", "false");

        // The framework resolves the user-secrets file from APPDATA, on Linux too, before it falls back to
        // HOME.
        Environment.SetEnvironmentVariable("APPDATA", EmptyAppDataRoot);
    }
}
