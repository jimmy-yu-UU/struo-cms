using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Infrastructure.DependencyInjection;
using Xunit;

namespace Struo.Tests.Query;

public sealed class StruoQueryOptionsValidatorTests
{
    private static readonly StruoQueryOptionsValidator Validator = new();

    [Fact]
    public void Shipped_defaults_are_valid() =>
        Validator.Validate(null, new StruoQueryOptions()).Succeeded.Should().BeTrue();

    [Fact]
    public void Default_limit_equal_to_max_limit_is_valid() =>
        Validator.Validate(null, new StruoQueryOptions { DefaultLimit = 100, MaxLimit = 100 }).Succeeded.Should().BeTrue();

    [Fact]
    public void Default_limit_above_max_limit_fails_naming_both_keys()
    {
        var result = Validator.Validate(null, new StruoQueryOptions { DefaultLimit = 200, MaxLimit = 100 });

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Query:DefaultLimit").And.Contain("Query:MaxLimit");
    }

    [Fact]
    public void Startup_resolution_throws_when_default_limit_exceeds_max_limit()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Query:DefaultLimit"] = "200",
            ["Query:MaxLimit"] = "100",
        }).Build();
        var services = new ServiceCollection().AddSingleton<IConfiguration>(config).AddStruoData();
        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IOptions<StruoQueryOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Query:DefaultLimit*Query:MaxLimit*");
    }
}
