using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Api.Http;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// Several creates of one language code that race each other: the unique index on the code lets exactly
// one commit, and every loser answers 409 CONFLICT with the fixed unique-violation message, never 500.
[Collection("ApiIntegration")]
public sealed class ConcurrentLanguageCreateTests(ApiFactory factory)
{
    private const int Racers = 6;

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static string NewCode() => $"cx{Guid.NewGuid():N}"[..12];

    private static async Task<(HttpStatusCode Status, string Body)[]> RaceAsync(HttpClient client, string[] codes)
    {
        var start = new TaskCompletionSource();
        var calls = codes.Select(async code =>
        {
            await start.Task;
            var resp = await client.PostAsJsonAsync("/api/items/language",
                new { code, name = code, isDefault = false, enabled = true, sort = 90 });
            return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }).ToArray();
        start.SetResult();
        return await Task.WhenAll(calls);
    }

    private static async Task<int> CountRowsAsync(HttpClient client, string code)
    {
        var list = Root(await client.GetStringAsync("/api/items/language?limit=200")).GetProperty("data");
        return list.EnumerateArray().Count(r =>
            string.Equals(r.GetProperty("code").GetString(), code, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Racing_creates_of_one_code_commit_once_and_the_rest_are_409_CONFLICT()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var code = NewCode();

        var results = await RaceAsync(admin, Enumerable.Repeat(code, Racers).ToArray());

        var summary = string.Join(" | ", results.Select(r => $"{(int)r.Status}"));
        results.Count(r => r.Status == HttpStatusCode.Created).Should().Be(1, summary);
        foreach (var loser in results.Where(r => r.Status != HttpStatusCode.Created))
        {
            loser.Status.Should().Be(HttpStatusCode.Conflict, loser.Body);
            var error = Root(loser.Body).GetProperty("error");
            error.GetProperty("code").GetString().Should().Be("CONFLICT");
            error.GetProperty("message").GetString().Should().Be(DomainErrorMap.UniqueViolationMessage);
        }
        (await CountRowsAsync(admin, code)).Should().Be(1);
    }
}
