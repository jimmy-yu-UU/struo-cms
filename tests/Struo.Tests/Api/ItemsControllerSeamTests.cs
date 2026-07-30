// tests/Struo.Tests/Api/ItemsControllerSeamTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Struo.Api.Controllers;
using Struo.Application.Query;
using Struo.Application.Revisions;
using Struo.Application.Security;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Proves <see cref="ItemsController"/> depends on the <see cref="IItemUseCases"/> seam
/// rather than the concrete <see cref="ItemService"/> — the controller is constructible with a
/// hand-rolled fake use-case layer (no DI, no WebApplicationFactory), and its list action delegates
/// to that seam and returns the fixed page in a 200 envelope.
/// </summary>
public class ItemsControllerSeamTests
{
    private sealed class FakePermissions : IPermissionService
    {
        public bool CanRead(string collection) => true;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public IReadOnlyCollection<string> ReadableFields(string collection, IEnumerable<string> allFieldNames)
            => allFieldNames.ToArray();
    }

    private sealed class FakeUseCases : IItemUseCases
    {
        private readonly PagedResult _page;
        public string? LastCollection { get; private set; }

        public FakeUseCases(PagedResult page) => _page = page;

        public Task<PagedResult> QueryAsync(
            string collection, QueryModel raw, string? locale = null,
            DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
        {
            LastCollection = collection;
            return Task.FromResult(_page);
        }

        public Task<IReadOnlyDictionary<string, object?>?> GetAsync(
            string collection, string id, DeepSpec? deep = null, string? locale = null,
            DeletedFilter deleted = DeletedFilter.Exclude, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);

        public Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>());

        public Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);

        public Task<bool> DeleteAsync(string collection, string id, bool purge = false, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyDictionary<string, object?>?> RestoreAsync(string collection, string id, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);

        public Task<IReadOnlyList<RevisionInfo>> ListRevisionsAsync(string collection, string id, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RevisionInfo>>([]);

        public Task<RevisionRecord?> GetRevisionAsync(string collection, string id, long revisionNumber, CancellationToken ct = default)
            => Task.FromResult<RevisionRecord?>(null);

        public Task<IReadOnlyDictionary<string, object?>?> RevertAsync(string collection, string id, long revisionNumber, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, object?>?>(null);
    }

    [Fact]
    public async Task List_delegates_to_the_use_case_seam_and_returns_200_envelope()
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["id"] = 1 }
        };
        var page = new PagedResult(rows, Total: 1, Limit: 25, Offset: 0);

        var useCases = new FakeUseCases(page);
        var controller = new ItemsController(useCases, new FakePermissions())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var result = await controller.List("article", CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<Struo.Api.Http.PagedResult>().Subject;
        envelope.Total.Should().Be(1);
        envelope.Limit.Should().Be(25);
        envelope.Offset.Should().Be(0);
        useCases.LastCollection.Should().Be("article");
    }
}
