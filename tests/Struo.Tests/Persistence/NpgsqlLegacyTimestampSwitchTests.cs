using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Persistence;

public class NpgsqlLegacyTimestampSwitchTests
{
    private const string SwitchName = "Npgsql.EnableLegacyTimestampBehavior";

    // SqlSugar's PostgreSQL provider sets the switch when a client's Ado is first used, with no
    // connection opened. The product does not set it; the UtcInstantConverter design depends on it.
    [Fact]
    public void SqlSugar_PostgreSQL_provider_enables_the_legacy_timestamp_switch()
    {
        var options = new DatabaseOptions
        {
            DbType = StruoDbType.PostgreSQL,
            ConnectionString = "Host=127.0.0.1;Port=1;Database=switch_probe;Username=probe;Password=probe"
        };
        using var client = SqlSugarClientFactory.Create(options, new TestCurrentUserAccessor(null));

        _ = client.Ado;

        var present = AppContext.TryGetSwitch(SwitchName, out var enabled);
        Assert.True(present && enabled,
            $"{SwitchName} is {(present ? enabled.ToString() : "absent")} after a PostgreSQL SqlSugar client " +
            "was created. The UTC instant converter relies on SqlSugar setting it; see " +
            "docs/ai/decisions/timestamptz-columns-are-utc-instants.md.");
    }
}
