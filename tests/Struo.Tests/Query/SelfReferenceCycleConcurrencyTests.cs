// tests/Struo.Tests/Query/SelfReferenceCycleConcurrencyTests.cs
using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Api.Http;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Struo.Tests.Support.Seeding;
using Xunit;
using File = Struo.Infrastructure.Files.File;
using FileTranslation = Struo.Infrastructure.Files.FileTranslation;

namespace Struo.Tests.Query;

/// <summary>
/// Two writers, each with its own connection and transaction, move media folders at the same time. A gate
/// holds each writer at its first locked read until both have written their own row, so both reach the
/// cycle check with an uncommitted write pending. Whatever the interleaving, the stored rows must not
/// contain a cycle afterwards and at most one writer may succeed.
/// </summary>
public sealed class SelfReferenceCycleConcurrencyTests : IDisposable
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(3);

    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();
    private readonly ItemService _setup;
    private readonly ItemService _writerA;
    private readonly ItemService _writerB;
    private readonly ISqlSugarClient _reader;

    public SelfReferenceCycleConcurrencyTests()
    {
        var gate = new ArrivalGate(2, GateTimeout);
        _setup = Build(true, null);
        _writerA = Build(false, gate);
        _writerB = Build(false, gate);
        _reader = SqlSugarClientFactory.Create(_database.Options(), new TestCurrentUserAccessor(Guid.Empty));
    }

    public void Dispose() => _database.Dispose();

    private ItemService Build(bool createTables, ArrivalGate? gate)
    {
        var currentUser = new TestCurrentUserAccessor(Guid.Empty);
        var db = SqlSugarClientFactory.Create(_database.Options(), currentUser);
        if (createTables)
        {
            db.CodeFirst.InitTables<MediaFolder>();
            db.CodeFirst.InitTables<File>();
            db.CodeFirst.InitTables<FileTranslation>();
            db.CodeFirst.InitTables<Language>();
            db.CodeFirst.InitTables<Revision>();
            LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();
        }

        var types = new[] { typeof(MediaFolder), typeof(File) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["mediafolder"] = typeof(MediaFolder),
            ["file"] = typeof(File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        IItemRepository repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        if (gate is not null) repo = GatedRepository.Wrap(repo, gate);
        return TestItemService.Create(
            repo, provider, registry, graph, new RelationExpander(repo, graph, new StruoQueryOptions()), graph,
            new LanguageProvider(db), currentUser, new SqlSugarRevisionStore(db, currentUser),
            new RevisionSnapshotBuilder(repo, provider, registry, graph));
    }

    private static JsonElement BodyOf(object obj) => JsonSerializer.SerializeToElement(obj);

    private async Task<Guid> CreateAsync(string name, Guid? parent = null)
    {
        var created = await _setup.CreateAsync("mediafolder", BodyOf(new { name, parentId = parent }));
        return (Guid)created["id"]!;
    }

    private enum Outcome { Moved, CycleRejected, DeadlockConflict, Other }

    private static async Task<(Outcome Outcome, Exception? Error)> MoveAsync(ItemService svc, Guid id, Guid parent)
    {
        try
        {
            await svc.UpdateAsync("mediafolder", id.ToString(), BodyOf(new { parentId = parent }));
            return (Outcome.Moved, null);
        }
        catch (QueryException ex) when (ex.Message.Contains("cycle"))
        {
            return (Outcome.CycleRejected, ex);
        }
        catch (Exception ex) when (StruoExceptionHandler.Map(ex, true).Status == 409)
        {
            return (Outcome.DeadlockConflict, ex);
        }
        catch (Exception ex)
        {
            return (Outcome.Other, ex);
        }
    }

    private async Task AssertNoCycleAsync()
    {
        var rows = await _reader.Queryable<MediaFolder>().ToListAsync();
        var parentOf = rows.ToDictionary(r => r.Id, r => r.ParentId);
        foreach (var row in rows)
        {
            var seen = new HashSet<Guid> { row.Id };
            for (var next = row.ParentId; next is { } id; next = parentOf.GetValueOrDefault(id))
                seen.Add(id).Should().BeTrue($"folder {row.Name} must not be its own ancestor");
        }
    }

    private static void AssertAtMostOneMoved(params (Outcome Outcome, Exception? Error)[] results)
    {
        results.Where(r => r.Outcome == Outcome.Other).Select(r => r.Error!.ToString()).Should().BeEmpty();
        results.Count(r => r.Outcome == Outcome.Moved).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Opposite_moves_never_leave_a_cycle()
    {
        var x = await CreateAsync("X");
        var y = await CreateAsync("Y");

        var a = Task.Run(() => MoveAsync(_writerA, x, y));
        var b = Task.Run(() => MoveAsync(_writerB, y, x));
        var results = await Task.WhenAll(a, b);

        AssertAtMostOneMoved(results);
        await AssertNoCycleAsync();
        AssertDeadlockOnServers(results);
    }

    // The gate makes both writers hold an uncommitted write before either reads the other's row, so on a
    // backend with row locks each waits for the other and the database ends one of them as a deadlock victim.
    private static void AssertDeadlockOnServers(params (Outcome Outcome, Exception? Error)[] results)
    {
        if (!TestBackend.IsLive) return;
        results.Select(r => r.Outcome).Should().BeEquivalentTo(new[] { Outcome.Moved, Outcome.DeadlockConflict });
    }

    // W <- Y and X <- D. A moves X under Y; B moves W under D. Each writer's own row and new parent are
    // different from the other's (X, Y versus W, D), so only the ancestor chain, W for A and X for B,
    // overlaps. Both commits together would give X -> Y -> W -> D -> X.
    [Fact]
    public async Task Moves_whose_only_overlap_is_further_up_the_chain_never_leave_a_cycle()
    {
        var w = await CreateAsync("W");
        var y = await CreateAsync("Y", w);
        var x = await CreateAsync("X");
        var d = await CreateAsync("D", x);

        var a = Task.Run(() => MoveAsync(_writerA, x, y));
        var b = Task.Run(() => MoveAsync(_writerB, w, d));
        var results = await Task.WhenAll(a, b);

        AssertAtMostOneMoved(results);
        await AssertNoCycleAsync();
        AssertDeadlockOnServers(results);
    }

    [Fact]
    public async Task Independent_moves_both_succeed()
    {
        var a1 = await CreateAsync("A1");
        var a2 = await CreateAsync("A2");
        var b1 = await CreateAsync("B1");
        var b2 = await CreateAsync("B2");

        var results = await Task.WhenAll(
            Task.Run(() => MoveAsync(_writerA, a1, a2)),
            Task.Run(() => MoveAsync(_writerB, b1, b2)));

        results.Select(r => r.Outcome).Should().AllBeEquivalentTo(Outcome.Moved);
        await AssertNoCycleAsync();
    }

    /// <summary>Releases its waiters once <c>parties</c> have arrived, or after the timeout.</summary>
    private sealed class ArrivalGate(int parties, TimeSpan timeout)
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _arrived) >= parties) _release.TrySetResult();
            await Task.WhenAny(_release.Task, Task.Delay(timeout));
        }
    }

    /// <summary>Forwards every call and holds the first locked read of its writer at the gate.</summary>
    private class GatedRepository : DispatchProxy
    {
        private IItemRepository _inner = null!;
        private ArrivalGate _gate = null!;
        private int _held;

        public static IItemRepository Wrap(IItemRepository inner, ArrivalGate gate)
        {
            var proxy = Create<IItemRepository, GatedRepository>();
            var self = (GatedRepository)(object)proxy;
            self._inner = inner;
            self._gate = gate;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name == nameof(IItemRepository.GetByIdLockedAsync)
                && Interlocked.Exchange(ref _held, 1) == 0)
                return HoldThenAsync(targetMethod, args);
            return Forward(targetMethod, args);
        }

        private async Task<object?> HoldThenAsync(MethodInfo method, object?[]? args)
        {
            await _gate.ArriveAsync();
            return await (Task<object?>)Forward(method, args)!;
        }

        private object? Forward(MethodInfo method, object?[]? args)
        {
            try
            {
                return method.Invoke(_inner, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        }
    }
}
