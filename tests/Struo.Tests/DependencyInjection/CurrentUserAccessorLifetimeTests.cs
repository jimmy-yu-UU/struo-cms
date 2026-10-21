using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Infrastructure.DependencyInjection;
using Xunit;

namespace Struo.Tests.DependencyInjection;

/// <summary>
/// The singleton ISqlSugarClient captures the current-user accessor once, so a scoped accessor must
/// stop the host with a clear error in every environment, not only where scope validation is on.
/// </summary>
public class CurrentUserAccessorLifetimeTests
{
    private sealed class FixedAccessor : ICurrentUserAccessor
    {
        public Guid? GetCurrentUserId() => null;
    }

    private static ServiceProvider Build(Action<IServiceCollection> registerAccessor)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = "Data Source=:memory:"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddStruoInfrastructure();
        registerAccessor(services);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void A_scoped_accessor_is_refused_when_the_client_is_built()
    {
        using var provider = Build(s => s.AddScoped<ICurrentUserAccessor, FixedAccessor>());

        var act = () => provider.GetRequiredService<ISqlSugarClient>();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*ICurrentUserAccessor is registered as Scoped*");
    }

    [Theory]
    [InlineData(ServiceLifetime.Singleton)]
    [InlineData(ServiceLifetime.Transient)]
    public void A_non_scoped_accessor_is_accepted(ServiceLifetime lifetime)
    {
        using var provider = Build(s => s.Add(new ServiceDescriptor(
            typeof(ICurrentUserAccessor), typeof(FixedAccessor), lifetime)));

        provider.GetRequiredService<ISqlSugarClient>().Should().NotBeNull();
    }
}
