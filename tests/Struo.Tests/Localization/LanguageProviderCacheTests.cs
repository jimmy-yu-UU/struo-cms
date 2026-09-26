using System.Collections.Concurrent;
using System.Reflection;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Localization;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

// LanguageProvider's per-scope cache must load at most once even under concurrent GraphQL
// resolvers. The cache field is a Lazy<IReadOnlyList<LanguageInfo>> (ExecutionAndPublication), so
// concurrent callers block on the same load instead of each racing to run the underlying query and
// observe different list instances. Invalidate() swaps in a fresh Lazy rather than mutating the old
// value (immutability rule).
public class LanguageProviderCacheTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly LanguageProvider _provider;

    public LanguageProviderCacheTests()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(_db, TestLocalization.Default).GetAwaiter().GetResult();
        _provider = new LanguageProvider(_db);
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public void Cache_is_backed_by_a_lazy_so_the_load_is_atomic()
    {
        var lazyField = typeof(LanguageProvider)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType == typeof(Lazy<IReadOnlyList<LanguageInfo>>));

        lazyField.Should().NotBeNull(
            "the per-scope cache must be a Lazy<IReadOnlyList<LanguageInfo>> so concurrent callers cannot double-load");
    }

    // Every concurrent caller sees the SAME cached list instance: Lazy<T> with
    // ExecutionAndPublication publishes exactly one result to every racing caller.
    [Fact]
    public async Task Concurrent_Enabled_returns_a_single_shared_instance()
    {
        var results = new ConcurrentBag<IReadOnlyList<LanguageInfo>>();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 32),
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            (_, _) => { results.Add(_provider.Enabled()); return ValueTask.CompletedTask; });

        results.Distinct(ReferenceEqualityComparer.Instance).Should().HaveCount(1);
    }

    [Fact]
    public void Enabled_reflects_seeded_languages()
    {
        _provider.Enabled().Select(l => l.Code).Should().Contain(["en", "zh-TW"]);
        _provider.DefaultCode().Should().Be("en");
        _provider.IsEnabled("EN").Should().BeTrue();       // case-insensitive
        _provider.IsEnabled("fr").Should().BeFalse();
    }

    [Fact]
    public async Task Invalidate_swaps_in_a_fresh_load_without_mutating_the_old_snapshot()
    {
        var before = _provider.Enabled();
        before.Any(l => l.Code == "fr").Should().BeFalse();

        await _db.Insertable(new Language
        {
            Code = "fr", Name = "Français", IsDefault = false, Enabled = true, Sort = 9,
        }).ExecuteCommandAsync();

        // Not yet visible: the cached snapshot is untouched.
        _provider.IsEnabled("fr").Should().BeFalse();
        before.Any(l => l.Code == "fr").Should().BeFalse();

        _provider.Invalidate();

        var after = _provider.Enabled();
        ReferenceEquals(after, before).Should().BeFalse("Invalidate must swap in a fresh Lazy, not reuse the old value");
        after.Any(l => l.Code == "fr").Should().BeTrue();
        before.Any(l => l.Code == "fr").Should().BeFalse("the previously returned snapshot must remain unmutated");
    }
}
