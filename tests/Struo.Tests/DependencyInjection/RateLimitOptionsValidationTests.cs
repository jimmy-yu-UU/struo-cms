using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Struo.Api.Hosting;
using Xunit;

namespace Struo.Tests.DependencyInjection;

/// <summary>
/// The four <c>RateLimiting:*</c> sections are validated at host start, whatever <c>Enabled</c> says, and
/// each failure names the full configuration key and the allowed range. The host here is built from the
/// same <c>AddStruoOptions</c> registration the application uses and started directly, which surfaces the
/// <see cref="OptionsValidationException"/> the start-up validation throws.
/// </summary>
public sealed class RateLimitOptionsValidationTests
{
    private static async Task<Exception?> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddStruoOptions();
        using var host = builder.Build();
        try
        {
            await host.StartAsync();
            await host.StopAsync();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // Each options type that fails is its own OptionsValidationException, gathered by the host into one
    // AggregateException.
    private static IEnumerable<string> FailuresOf(Exception? error) => error switch
    {
        null => [],
        OptionsValidationException invalid => invalid.Failures,
        AggregateException aggregate => aggregate.InnerExceptions.SelectMany(FailuresOf),
        _ => FailuresOf(error.InnerException),
    };

    private static string Failures(Exception? error)
    {
        var failures = FailuresOf(error).ToList();
        failures.Should().NotBeEmpty("an out-of-range RateLimiting value must fail at start-up");
        return string.Join(" ", failures);
    }

    [Fact]
    public async Task The_shipped_defaults_start()
    {
        (await StartAsync([])).Should().BeNull();
    }

    [Theory]
    [InlineData("Login", "PermitLimit", "0", "between 1 and 1000000")]
    [InlineData("Login", "PermitLimit", "-1", "between 1 and 1000000")]
    [InlineData("Login", "PermitLimit", "1000001", "between 1 and 1000000")]
    [InlineData("Login", "WindowSeconds", "0", "between 1 and 86400")]
    [InlineData("Login", "WindowSeconds", "-1", "between 1 and 86400")]
    [InlineData("Login", "WindowSeconds", "86401", "between 1 and 86400")]
    [InlineData("LoginAccount", "PermitLimit", "0", "between 1 and 1000000")]
    [InlineData("LoginAccount", "PermitLimit", "-1", "between 1 and 1000000")]
    [InlineData("LoginAccount", "PermitLimit", "1000001", "between 1 and 1000000")]
    [InlineData("LoginAccount", "WindowSeconds", "0", "between 1 and 86400")]
    [InlineData("LoginAccount", "WindowSeconds", "-1", "between 1 and 86400")]
    [InlineData("LoginAccount", "WindowSeconds", "86401", "between 1 and 86400")]
    [InlineData("Password", "PermitLimit", "0", "between 1 and 1000000")]
    [InlineData("Password", "PermitLimit", "-1", "between 1 and 1000000")]
    [InlineData("Password", "PermitLimit", "1000001", "between 1 and 1000000")]
    [InlineData("Password", "WindowSeconds", "0", "between 1 and 86400")]
    [InlineData("Password", "WindowSeconds", "-1", "between 1 and 86400")]
    [InlineData("Password", "WindowSeconds", "86401", "between 1 and 86400")]
    [InlineData("LoginConcurrency", "PermitLimit", "0", "between 1 and 1000")]
    [InlineData("LoginConcurrency", "PermitLimit", "-1", "between 1 and 1000")]
    [InlineData("LoginConcurrency", "PermitLimit", "1001", "between 1 and 1000")]
    [InlineData("LoginConcurrency", "QueueLimit", "-1", "between 0 and 10000")]
    [InlineData("LoginConcurrency", "QueueLimit", "10001", "between 0 and 10000")]
    public async Task An_out_of_range_value_fails_start_up_naming_the_key_and_the_range(
        string section, string key, string value, string range)
    {
        foreach (var enabled in new[] { "true", "false" })
        {
            var error = await StartAsync(new()
            {
                [$"RateLimiting:{section}:Enabled"] = enabled,
                [$"RateLimiting:{section}:{key}"] = value,
            });

            var text = Failures(error);
            text.Should().Contain($"RateLimiting:{section}:{key}", $"Enabled={enabled}");
            text.Should().Contain(range, $"Enabled={enabled}");
        }
    }

    [Theory]
    [InlineData("Login", "PermitLimit", "1")]
    [InlineData("Login", "PermitLimit", "1000000")]
    [InlineData("Login", "WindowSeconds", "1")]
    [InlineData("Login", "WindowSeconds", "86400")]
    [InlineData("LoginAccount", "PermitLimit", "1")]
    [InlineData("LoginAccount", "PermitLimit", "1000000")]
    [InlineData("LoginAccount", "WindowSeconds", "1")]
    [InlineData("LoginAccount", "WindowSeconds", "86400")]
    [InlineData("Password", "PermitLimit", "1")]
    [InlineData("Password", "PermitLimit", "1000000")]
    [InlineData("Password", "WindowSeconds", "1")]
    [InlineData("Password", "WindowSeconds", "86400")]
    [InlineData("LoginConcurrency", "PermitLimit", "1")]
    [InlineData("LoginConcurrency", "PermitLimit", "1000")]
    [InlineData("LoginConcurrency", "QueueLimit", "0")]
    [InlineData("LoginConcurrency", "QueueLimit", "10000")]
    public async Task A_value_on_the_range_boundary_starts(string section, string key, string value)
    {
        (await StartAsync(new() { [$"RateLimiting:{section}:{key}"] = value })).Should().BeNull();
    }

    [Fact]
    public async Task Every_failing_key_is_reported_together()
    {
        var error = await StartAsync(new()
        {
            ["RateLimiting:Login:PermitLimit"] = "0",
            ["RateLimiting:Password:WindowSeconds"] = "0",
        });

        var text = Failures(error);
        text.Should().Contain("RateLimiting:Login:PermitLimit").And.Contain("RateLimiting:Password:WindowSeconds");
    }
}
