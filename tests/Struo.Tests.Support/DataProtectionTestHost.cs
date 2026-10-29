using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Struo.Tests.Support;

/// <summary>
/// ASP.NET Core's <c>DataProtectionHostedService</c> reads the key ring when the host starts, which opens a
/// connection to the configured Redis. A test host that configures Redis without a live server removes that
/// hosted service so start-up never waits on the connection.
/// </summary>
public static class DataProtectionTestHost
{
    private const string HostedServiceName = "DataProtectionHostedService";

    public static void RemoveKeyRingLoader(IServiceCollection services)
    {
        var descriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService)
                        && d.ImplementationType?.Name == HostedServiceName)
            .ToList();
        if (descriptors.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one {HostedServiceName} registration but found {descriptors.Count}.");
        }

        services.Remove(descriptors[0]);
    }
}
