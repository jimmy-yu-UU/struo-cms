using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;
using Struo.Infrastructure.DependencyInjection;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Migrations.Schema;
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
    public void Container_built_client_derives_the_file_translations_unique_index()
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

        var table = client.EntityMaintenance.GetTableName<FileTranslation>();
        IndexCatalog.Read(client, StruoDbType.Sqlite, table).Should().Contain(i => i.IsUnique
            && i.Columns.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(new[]
            {
                client.EntityMaintenance.GetDbColumnName<FileTranslation>(nameof(FileTranslation.FileId)),
                client.EntityMaintenance.GetDbColumnName<FileTranslation>(nameof(FileTranslation.Locale)),
            }.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase));
    }
}
