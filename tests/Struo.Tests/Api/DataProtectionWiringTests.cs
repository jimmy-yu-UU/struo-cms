using AwesomeAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The DataProtection key ring lives in Redis when <c>Redis:ConnectionString</c> is set, and stays in the
/// default file location otherwise; the application name always comes from
/// <c>DataProtection:ApplicationName</c>. These tests assert the registration only: with
/// <c>abortConnect=false</c> and nothing resolving the key ring, no connection to Redis is opened.
/// </summary>
[Collection("ApiIntegration")]
public class DataProtectionWiringTests(ApiFactory factory)
{
    private IServiceProvider StartHost(string redis, string? applicationName = null)
    {
        var host = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
        {
            var settings = new Dictionary<string, string?> { ["Redis:ConnectionString"] = redis };
            if (applicationName is not null) settings["DataProtection:ApplicationName"] = applicationName;
            c.AddInMemoryCollection(settings);
        }));
        return host.Services;
    }

    [Fact]
    public void Without_Redis_no_repository_is_set_so_the_default_file_location_applies()
    {
        var services = StartHost("");

        var repository = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;

        repository.Should().BeNull();
    }

    [Fact]
    public void With_Redis_the_key_ring_is_stored_in_Redis()
    {
        var services = StartHost("127.0.0.1:1,abortConnect=false");

        var repository = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlRepository;

        repository.Should().BeOfType<RedisXmlRepository>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1:1,abortConnect=false")]
    public void The_application_name_defaults_to_StruoCMS(string redis)
    {
        var services = StartHost(redis);

        services.GetRequiredService<IOptions<DataProtectionOptions>>().Value
            .ApplicationDiscriminator.Should().Be("StruoCMS");
    }

    [Fact]
    public void The_application_name_comes_from_configuration()
    {
        var services = StartHost("", "acme-cms");

        services.GetRequiredService<IOptions<DataProtectionOptions>>().Value
            .ApplicationDiscriminator.Should().Be("acme-cms");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_application_name_fails_validation(string name)
    {
        var services = StartHost("", name);

        var act = () => services.GetRequiredService<IOptions<StruoDataProtectionOptions>>().Value;

        act.Should().Throw<OptionsValidationException>()
            .Which.Failures.Should().ContainSingle(f => f.Contains("DataProtection:ApplicationName"));
    }
}
