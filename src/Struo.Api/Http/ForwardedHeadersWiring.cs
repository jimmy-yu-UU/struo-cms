using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Struo.Api.Http;

/// <summary>Bound from the <c>ForwardedHeaders</c> section. Off by default: the headers are honoured
/// only when <see cref="Enabled"/> is true and the connection comes from a listed proxy or network.</summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; }

    /// <summary>Proxy addresses whose X-Forwarded-* headers are trusted.</summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>CIDR ranges (e.g. 172.16.0.0/12) whose X-Forwarded-* headers are trusted.</summary>
    public string[] KnownNetworks { get; set; } = [];
}

public static class ForwardedHeadersWiring
{
    private const string ProxiesKey = SectionName + ":KnownProxies";
    private const string NetworksKey = SectionName + ":KnownNetworks";
    private const string SectionName = ForwardedHeadersSettings.SectionName;

    /// <summary>Binds the section and, when enabled, derives the middleware options from it. Resolved
    /// lazily so the final configuration (environment, command line) is what gets read.</summary>
    public static IServiceCollection AddStruoForwardedHeaders(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<ForwardedHeadersSettings>().Bind(config.GetSection(SectionName));
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IOptions<ForwardedHeadersSettings>>((options, settings) => Apply(options, settings.Value));
        return services;
    }

    /// <summary>Applies the forwarded headers when enabled; must precede everything that reads the
    /// client address, scheme or host. Throws at startup, naming the offending key, when an entry
    /// does not parse or nothing is trusted.</summary>
    public static IApplicationBuilder UseStruoForwardedHeaders(this IApplicationBuilder app)
    {
        var settings = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value;
        if (!settings.Enabled) return app;

        // Resolving the options here surfaces a bad entry before the first request.
        _ = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        return app.UseForwardedHeaders();
    }

    private static void Apply(ForwardedHeadersOptions options, ForwardedHeadersSettings settings)
    {
        if (!settings.Enabled) return;

        if (settings.KnownProxies.Length == 0 && settings.KnownNetworks.Length == 0)
            throw new InvalidOperationException(
                $"{SectionName}:Enabled requires {ProxiesKey} or {NetworksKey}; without either no sender is trusted.");

        var proxies = settings.KnownProxies.Select(ParseProxy).ToList();
        var networks = settings.KnownNetworks.Select(ParseNetwork).ToList();

        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            | ForwardedHeaders.XForwardedHost;
        // The defaults trust loopback only; the configured lists replace them.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var p in proxies) options.KnownProxies.Add(p);
        foreach (var n in networks) options.KnownIPNetworks.Add(n);
    }

    private static IPAddress ParseProxy(string value, int index) =>
        IPAddress.TryParse(value, out var ip)
            ? ip
            : throw new InvalidOperationException($"{ProxiesKey}:{index} is not an IP address: '{value}'.");

    private static System.Net.IPNetwork ParseNetwork(string value, int index) =>
        System.Net.IPNetwork.TryParse(value, out var network)
            ? network
            : throw new InvalidOperationException(
                $"{NetworksKey}:{index} is not a CIDR range such as 172.16.0.0/12: '{value}'.");
}
