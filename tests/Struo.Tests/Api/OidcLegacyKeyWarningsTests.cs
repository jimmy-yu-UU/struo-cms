using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Struo.Api.Auth;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

public sealed class OidcLegacyKeyWarningsTests
{
    private static ListLogger Run(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value)).Build();
        var logger = new ListLogger();
        OidcLegacyKeyWarnings.Log(config, logger);
        return logger;
    }

    [Fact]
    public void Provider_shaped_configuration_logs_nothing() =>
        Run(("Oidc:ReturnUrlDefault", "/"), ("Oidc:Providers:microsoft:Enabled", "true"),
            ("Oidc:Providers:microsoft:ClientSecret", "s")).Entries.Should().BeEmpty();

    [Fact]
    public void Flat_keys_log_one_warning_naming_the_keys_and_never_the_values()
    {
        var entry = Run(
                ("Oidc:Enabled", "true"), ("Oidc:Authority", "https://secret-authority.example"),
                ("Oidc:ClientSecret", "super-secret-value"), ("Oidc:Providers:google:Enabled", "false"))
            .Entries.Should().ContainSingle().Which;

        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("Oidc:Enabled").And.Contain("Oidc:Authority").And.Contain("Oidc:ClientSecret")
            .And.Contain("Oidc:Providers:<id>").And.Contain("ignored");
        entry.Message.Should().NotContain("super-secret-value").And.NotContain("secret-authority")
            .And.NotContain("Oidc:ClientId");
    }

    [Theory]
    [InlineData("CallbackPath")]
    [InlineData("Scopes:0")]
    [InlineData("Prompt")]
    [InlineData("AllowedTenantId")]
    [InlineData("AllowedEmailDomains:0")]
    [InlineData("RequireEmailVerified")]
    [InlineData("ClientId")]
    public void Each_legacy_key_triggers_the_warning(string key) =>
        Run(($"Oidc:{key}", "x")).Entries.Should().ContainSingle();
}
