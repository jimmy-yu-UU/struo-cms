using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Query;
using Struo.Infrastructure.Files;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Concurrency;

/// <summary>
/// Two requests move media folders in opposite directions at the same moment through the REST stack.
/// A write interceptor holds each request after its body is merged until both have arrived, so the two
/// transactions start together. At most one request may succeed, the other answers 400 (cycle) or 409
/// (lost a database deadlock), and the stored rows never contain a cycle.
/// </summary>
[Collection("ApiIntegration")]
public class OppositeFolderMoveApiTests
{
    private sealed class StartTogetherInterceptor : IItemWriteInterceptor
    {
        private volatile TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        public volatile bool Armed;

        public void Arm()
        {
            _arrived = 0;
            _release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Armed = true;
        }

        public bool AppliesTo(string collection) =>
            string.Equals(collection, "MediaFolder", StringComparison.OrdinalIgnoreCase);

        public async Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
        {
            if (!Armed || context.Operation != ItemWriteOperation.Update) return;
            if (Interlocked.Increment(ref _arrived) >= 2) _release.TrySetResult();
            await Task.WhenAny(_release.Task, Task.Delay(TimeSpan.FromSeconds(10), ct));
        }
    }

    private sealed class Factory : StruoApiFactory
    {
        public StartTogetherInterceptor Gate { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<IItemWriteInterceptor>(Gate));
        }
    }

    private static async Task<Guid> CreateFolderAsync(HttpClient client, string name)
    {
        using var response = await client.PostAsJsonAsync("/api/items/mediafolder", new { name });
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("data").GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Opposite_moves_answer_at_most_one_200_and_never_leave_a_cycle()
    {
        using var factory = new Factory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var x = await CreateFolderAsync(client, $"X{attempt}");
            var y = await CreateFolderAsync(client, $"Y{attempt}");

            factory.Gate.Arm();
            var toY = Task.Run(() => client.PutAsJsonAsync($"/api/items/mediafolder/{x}", new { parentId = y }));
            var toX = Task.Run(() => client.PutAsJsonAsync($"/api/items/mediafolder/{y}", new { parentId = x }));
            using var first = await toY;
            using var second = await toX;
            factory.Gate.Armed = false;

            var statuses = new[] { first.StatusCode, second.StatusCode };
            statuses.Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.BadRequest
                                               || s == HttpStatusCode.Conflict);
            statuses.Count(s => s == HttpStatusCode.OK).Should().BeLessThanOrEqualTo(1);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var rowX = await db.Queryable<MediaFolder>().In(x).FirstAsync();
            var rowY = await db.Queryable<MediaFolder>().In(y).FirstAsync();
            (rowX.ParentId == y && rowY.ParentId == x).Should().BeFalse("the two folders must not be each other's parent");
        }
    }
}
