using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Production with Redis warns once when the key ring is stored unprotected and once when Redis has no
/// password. The warnings name configuration keys only, never the connection string.
/// </summary>
public sealed class DataProtectionWarningsTests
{
    private const string Unprotected = "DataProtection:Certificate:Path";
    private const string NoPassword = "password";

    private static List<string> Run(string environment, params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value)).Build();
        var logger = new ListLogger();
        DataProtectionWarnings.Log(config, new HostingEnvironment { EnvironmentName = environment }, logger);
        logger.Entries.Should().OnlyContain(e => e.Level == LogLevel.Warning);
        return logger.Entries.Select(e => e.Message).ToList();
    }

    [Fact]
    public void Production_with_a_password_less_Redis_and_no_certificate_logs_both_warnings_once()
    {
        var messages = Run("Production", ("Redis:ConnectionString", "redis.internal:6379"));

        messages.Should().HaveCount(2);
        messages.Should().ContainSingle(m => m.Contains(Unprotected));
        messages.Should().ContainSingle(m => m.Contains("Redis:ConnectionString") && m.Contains(NoPassword));
    }

    [Fact]
    public void A_certificate_silences_only_the_key_protection_warning()
    {
        var messages = Run("Production",
            ("Redis:ConnectionString", "redis.internal:6379"), (Unprotected, "/keys/ring.pfx"));

        messages.Should().ContainSingle().Which.Should().Contain(NoPassword);
    }

    [Fact]
    public void A_Redis_password_silences_only_the_password_warning()
    {
        var messages = Run("Production", ("Redis:ConnectionString", "redis.internal:6379,password=hunter2"));

        messages.Should().ContainSingle().Which.Should().Contain(Unprotected);
    }

    [Fact]
    public void A_certificate_and_a_Redis_password_log_nothing() =>
        Run("Production", ("Redis:ConnectionString", "redis.internal:6379,password=hunter2"),
            (Unprotected, "/keys/ring.pfx")).Should().BeEmpty();

    [Fact]
    public void Development_logs_nothing() =>
        Run("Development", ("Redis:ConnectionString", "redis.internal:6379")).Should().BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Production_without_Redis_logs_nothing(string redis) =>
        Run("Production", ("Redis:ConnectionString", redis)).Should().BeEmpty();

    [Fact]
    public void The_connection_string_and_its_password_never_reach_the_log()
    {
        var messages = Run("Production", ("Redis:ConnectionString", "secret-host.example:6379,password="));

        messages.Should().NotBeEmpty();
        messages.Should().NotContain(m => m.Contains("secret-host"));

        var withPassword = Run("Production", ("Redis:ConnectionString", "h:6379,password=hunter2"));
        withPassword.Should().NotContain(m => m.Contains("hunter2"));
    }

    [Fact]
    public void An_unparseable_connection_string_does_not_fail_startup_and_skips_the_password_warning()
    {
        var messages = Run("Production", ("Redis:ConnectionString", "host:6379,abortConnect=notabool"), (Unprotected, "/k.pfx"));

        messages.Should().BeEmpty();
    }
}
