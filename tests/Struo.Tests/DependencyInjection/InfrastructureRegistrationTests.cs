using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Infrastructure.DependencyInjection;
using Xunit;

namespace Struo.Tests.DependencyInjection;

public class InfrastructureRegistrationTests
{
    [Fact]
    public void AddStruoInfrastructure_registers_core_services()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = "Data Source=:memory:"
            })
            .Build();

        var services = new ServiceCollection();
        // Options now bind via BindConfiguration, which resolves IConfiguration from DI.
        services.AddSingleton<IConfiguration>(config);
        services.AddStruoInfrastructure();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<ICurrentUserAccessor>().Should().NotBeNull();
        scope.ServiceProvider.GetService<ISqlSugarClient>().Should().NotBeNull();
    }
}
