using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class MigrationHostClientTests
{
    // The host reads its version table through entity mapping, so its reflection cache is scoped to its
    // provider like every factory client's: the same key resolves to a separate entry per provider.
    [Theory]
    [InlineData(StruoDbType.Sqlite, StruoDbType.SqlServer)]
    [InlineData(StruoDbType.MySql, StruoDbType.PostgreSQL)]
    public void Clients_for_two_providers_keep_separate_reflection_cache_entries(StruoDbType first, StruoDbType second)
    {
        var key = "k" + Guid.NewGuid().ToString("N");
        using var a = MigrationHost.OpenClient(first, UnusedConnection(first));
        using var b = MigrationHost.OpenClient(second, UnusedConnection(second));

        a.Utilities.GetReflectionInoCacheInstance().GetOrCreate(key, () => first.ToString()).Should().Be(first.ToString());
        b.Utilities.GetReflectionInoCacheInstance().GetOrCreate(key, () => second.ToString()).Should().Be(second.ToString());
    }

    // Never opened; disposing still parses it, and Npgsql has no "Data Source" keyword.
    private static string UnusedConnection(StruoDbType dbType) =>
        dbType == StruoDbType.PostgreSQL ? "Host=unused" : "Data Source=unused";
}
