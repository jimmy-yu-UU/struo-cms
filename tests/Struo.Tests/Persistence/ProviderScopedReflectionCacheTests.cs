using AwesomeAssertions;
using SqlSugar;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Persistence;

public sealed class ProviderScopedReflectionCacheTests
{
    // A value type private to this class, so no other test shares the underlying cache instance.
    private sealed record Entry(string Value);

    private readonly string _key = "k" + Guid.NewGuid().ToString("N");
    private readonly ProviderScopedReflectionCache _sqlServer = new(DbType.SqlServer);
    private readonly ProviderScopedReflectionCache _mySql = new(DbType.MySql);

    [Fact]
    public void GetOrCreate_builds_one_entry_per_provider_for_the_same_key()
    {
        _sqlServer.GetOrCreate(_key, () => new Entry("sqlserver")).Value.Should().Be("sqlserver");
        _mySql.GetOrCreate(_key, () => new Entry("mysql")).Value.Should().Be("mysql");

        _sqlServer.GetOrCreate(_key, () => new Entry("again")).Value.Should().Be("sqlserver");
        _mySql.Get<Entry>(_key).Value.Should().Be("mysql");
    }

    [Fact]
    public void Add_ContainsKey_GetAllKey_and_Remove_stay_within_the_provider()
    {
        _sqlServer.Add(_key, new Entry("a"));
        _sqlServer.Add(_key + "-other", new Entry("b"));

        _sqlServer.ContainsKey<Entry>(_key).Should().BeTrue();
        _mySql.ContainsKey<Entry>(_key).Should().BeFalse();
        _sqlServer.GetAllKey<Entry>().Should().Contain([_key, _key + "-other"]);
        _mySql.GetAllKey<Entry>().Should().NotContain(k => k.StartsWith(_key, StringComparison.Ordinal));

        _sqlServer.Remove<Entry>(_key);

        _sqlServer.ContainsKey<Entry>(_key).Should().BeFalse();
        _sqlServer.ContainsKey<Entry>(_key + "-other").Should().BeTrue();
    }

    [Fact]
    public void A_timed_add_is_refused_like_the_shared_cache_refuses_it()
    {
        var act = () => _sqlServer.Add(_key, new Entry("a"), 60);

        act.Should().Throw<SqlSugarException>();
        _sqlServer.ContainsKey<Entry>(_key).Should().BeFalse();
    }
}
