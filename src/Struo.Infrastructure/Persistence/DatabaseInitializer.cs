using Microsoft.Extensions.Hosting;
using SqlSugar;

namespace Struo.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static void InitializeDevelopmentSchema(
        ISqlSugarClient client, IHostEnvironment environment, params Type[] entityTypes)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "InitTables is only permitted in the Development environment. " +
                $"Current environment: '{environment.EnvironmentName}'. " +
                "Production schema changes must go through reviewed migration scripts.");
        }

        if (entityTypes.Length == 0) return;
        client.CodeFirst.InitTables(entityTypes);
    }
}
