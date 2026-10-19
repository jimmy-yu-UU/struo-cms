using Struo.Tests.Support.Seeding;
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

// LanguageProvider's per-scope cache loads at most once even under concurrent GraphQL resolvers
// (callers share one load Task). Invalidate() swaps in a fresh load rather than mutating the
// published snapshot, and a failed load is discarded so the next access retries.
public sealed class LanguageProviderCacheTests : IDisposable
{
    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly LanguageProvider _provider;

    public LanguageProviderCacheTests()
    {
        _db = SqlSugarClientFactory.Create(
            _file.Options(),
            new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(_db, TestLocalization.Default).GetAwaiter().GetResult();
        _provider = new LanguageProvider(_db);
    }

    public void Dispose() => _file.Dispose();

    // Every concurrent caller sees the SAME cached list instance.
    [Fact]
    public async Task Concurrent_GetEnabledAsync_returns_a_single_shared_instance()
    {
        var results = new ConcurrentBag<IReadOnlyList<LanguageInfo>>();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 32),
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (_, _) => results.Add(await _provider.GetEnabledAsync()));

        results.Distinct(ReferenceEqualityComparer.Instance).Should().HaveCount(1);
    }

    [Fact]
    public async Task Enabled_reflects_seeded_languages()
    {
        (await _provider.GetEnabledAsync()).Select(l => l.Code).Should().Contain(["en", "zh-TW"]);
        (await _provider.DefaultCodeAsync()).Should().Be("en");
        (await _provider.IsEnabledAsync("EN")).Should().BeTrue();       // case-insensitive
        (await _provider.IsEnabledAsync("fr")).Should().BeFalse();
    }

    [Fact]
    public async Task CanonicalizeAsync_returns_the_configured_spelling_or_null()
    {
        (await _provider.CanonicalizeAsync("ZH-tw")).Should().Be("zh-TW");
        (await _provider.CanonicalizeAsync("en")).Should().Be("en");
        (await _provider.CanonicalizeAsync("fr")).Should().BeNull();
    }

    [Fact]
    public async Task Invalidate_swaps_in_a_fresh_load_without_mutating_the_old_snapshot()
    {
        var before = await _provider.GetEnabledAsync();
        before.Any(l => l.Code == "fr").Should().BeFalse();

        await _db.Insertable(new Language
        {
            Code = "fr", Name = "Français", IsDefault = false, Enabled = true, Sort = 9,
        }).ExecuteCommandAsync();

        // Not yet visible: the cached snapshot is untouched.
        (await _provider.IsEnabledAsync("fr")).Should().BeFalse();
        before.Any(l => l.Code == "fr").Should().BeFalse();

        _provider.Invalidate();

        var after = await _provider.GetEnabledAsync();
        ReferenceEquals(after, before).Should().BeFalse("Invalidate must swap in a fresh load, not reuse the old value");
        after.Any(l => l.Code == "fr").Should().BeTrue();
        before.Any(l => l.Code == "fr").Should().BeFalse("the previously returned snapshot must remain unmutated");
    }

    [Fact]
    public async Task A_language_disabled_in_the_same_scope_is_rejected_right_after_Invalidate()
    {
        (await _provider.IsEnabledAsync("zh-TW")).Should().BeTrue();

        await _db.Updateable<Language>().SetColumns(l => l.Enabled == false).Where(l => l.Code == "zh-TW")
            .ExecuteCommandAsync();
        _provider.Invalidate();

        (await _provider.IsEnabledAsync("zh-TW")).Should().BeFalse();
        (await _provider.CanonicalizeAsync("ZH-TW")).Should().BeNull();
    }

    [Fact]
    public async Task DefaultCodeAsync_throws_when_no_language_is_enabled()
    {
        await _db.Updateable<Language>().SetColumns(l => l.Enabled == false).Where(l => true).ExecuteCommandAsync();
        _provider.Invalidate();

        var act = () => _provider.DefaultCodeAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no enabled language*");
        (await _provider.GetEnabledAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task DefaultCodeAsync_falls_back_to_the_lowest_sort_when_no_row_is_marked_default()
    {
        await _db.Updateable<Language>().SetColumns(l => l.IsDefault == false).Where(l => true).ExecuteCommandAsync();
        _provider.Invalidate();

        (await _provider.DefaultCodeAsync()).Should().Be("en"); // en has Sort 1 in TestLocalization.Default
    }

    // A load that throws is not cached: once the cause is gone, the next access succeeds.
    [Fact]
    public async Task A_failed_load_is_not_cached_and_the_next_access_retries()
    {
        var emptyFile = TestBackend.CreateUnitDatabase();
        try
        {
            var db = SqlSugarClientFactory.Create(emptyFile.Options(), new TestCurrentUserAccessor(Guid.Empty));
            var provider = new LanguageProvider(db);

            var first = () => provider.GetEnabledAsync();
            await first.Should().ThrowAsync<Exception>();

            db.CodeFirst.InitTables<Language>();
            await LanguageSeeder.SeedAsync(db, TestLocalization.Default);

            (await provider.GetEnabledAsync()).Select(l => l.Code).Should().Contain("en");
        }
        finally
        {
            emptyFile.Dispose();
        }
    }
}
