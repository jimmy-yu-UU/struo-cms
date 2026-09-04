using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
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

    [Fact]
    public async Task Container_built_client_derives_the_file_translations_unique_index()
    {
        using var db = new SqliteTestDatabase();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DbType"] = "Sqlite",
                ["Database:ConnectionString"] = db.ConnectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddStruoInfrastructure();
        services.AddStruoMetadata(); // framework assembly only -> File + FileTranslation are scanned
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();

        client.CodeFirst.InitTables(typeof(Struo.Infrastructure.Revisions.Revision), typeof(FileTranslation));

        var descriptor = new TranslationSidecarDescriptor(
            client.EntityMaintenance.GetTableName(typeof(FileTranslation)),
            client.EntityMaintenance.GetDbColumnName(nameof(FileTranslation.FileId), typeof(FileTranslation)),
            client.EntityMaintenance.GetDbColumnName(nameof(FileTranslation.Locale), typeof(FileTranslation)));
        var act = () => SchemaGuard.AssertCriticalConstraintsAsync(client, [descriptor], default);
        await act.Should().NotThrowAsync();
    }
}
