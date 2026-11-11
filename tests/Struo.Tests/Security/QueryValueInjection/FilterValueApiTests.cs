// tests/Struo.Tests/Security/QueryValueInjection/FilterValueApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Localization;
using Struo.Tests.Support;
using Xunit;
using StruoFile = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>Filter values reach the database only as parsed values or bound parameters, on every entrance.</summary>
public sealed class FilterValueApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Widen = "0 OR 1=1";
    private const string QuoteBreak = "x' OR 1=1 -- -";
    private const string SlashBreak = "x\\' OR 1=1 -- -";

    private SqlCapture? _capture;

    private SqlCapture Capture => _capture ??= SqlCapture.Attach(factory.Services);

    private ISqlSugarClient Db => factory.Services.GetRequiredService<ISqlSugarClient>();

    private static string Q(string s) => Uri.EscapeDataString(s);

    private static async Task<(HttpStatusCode Status, JsonElement Root)> SendAsync(HttpClient c, HttpMethod method, string url, string? json = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (json is not null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await c.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, JsonDocument.Parse(text).RootElement.Clone());
    }

    private static Task<(HttpStatusCode Status, JsonElement Root)> GetAsync(HttpClient c, string url) =>
        SendAsync(c, HttpMethod.Get, url);

    private static List<string> Ids(JsonElement root) =>
        root.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("id").ToString()).ToList();

    private static string ErrorCode(JsonElement root) => root.GetProperty("error").GetProperty("code").GetString()!;

    private void NoPayloadInSql(string needle) =>
        Capture.All.Where(s => s.Contains(needle, StringComparison.OrdinalIgnoreCase) && !s.Contains("-- params:"))
            .Should().BeEmpty("the text must not be part of a statement");

    [Theory]
    [InlineData("id", "_in", Widen)]
    [InlineData("id", "_nin", Widen)]
    [InlineData("sort", "_in", Widen)]
    [InlineData("sort", "_nin", Widen)]
    [InlineData("enabled", "_in", Widen)]
    [InlineData("enabled", "_nin", Widen)]
    [InlineData("id", "_in", "abc")]
    [InlineData("id", "_eq", "abc")]
    [InlineData("sort", "_gt", "abc")]
    [InlineData("enabled", "_eq", "abc")]
    public async Task A_value_that_is_not_of_the_column_type_is_a_400(string field, string op, string value)
    {
        var anon = factory.CreateClient();
        Capture.Clear();
        var (status, root) = await GetAsync(anon, $"/api/items/language?filter[{field}][{op}]={Q(value)}");
        status.Should().Be(HttpStatusCode.BadRequest);
        ErrorCode(root).Should().Be("BAD_USER_INPUT");
        NoPayloadInSql("1=1");
    }

    [Fact]
    public async Task A_payload_cannot_widen_the_filter_or_the_trash()
    {
        var anon = factory.CreateClient();
        var live = Guid.CreateVersion7();
        var trashed = Guid.CreateVersion7();
        foreach (var (id, deleted) in new[] { (live, false), (trashed, true) })
            await Db.Insertable(new StruoFile
            {
                Id = id, StorageKey = $"qv/{id:N}", FileName = $"qv-{id:N}.bin", ContentType = "application/octet-stream",
                Size = 777001, Status = "published", DeletedAt = deleted ? DateTime.UtcNow : null
            }).ExecuteCommandAsync();

        var valid = await GetAsync(anon, "/api/items/file?limit=100&filter[size][_in]=777001");
        valid.Status.Should().Be(HttpStatusCode.OK);
        Ids(valid.Root).Should().Contain(live.ToString()).And.NotContain(trashed.ToString());

        foreach (var payload in new[] { "777001 OR 1=1", "777001 OR size=777001", "777001) OR (1=1" })
        {
            var r = await GetAsync(anon, "/api/items/file?limit=100&filter[size][_in]=" + Q(payload));
            r.Status.Should().Be(HttpStatusCode.BadRequest, payload);
        }

        var order = await GetAsync(anon, "/api/items/language?filter[code][_eq]=zz-nomatch&filter[id][_in]=" + Q(Widen));
        order.Status.Should().Be(HttpStatusCode.BadRequest);
        var reversed = await GetAsync(anon, "/api/items/language?filter[id][_in]=" + Q(Widen) + "&filter[code][_eq]=zz-nomatch");
        reversed.Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_query_that_reads_another_table_is_refused_before_any_statement_runs()
    {
        await factory.EnsureAdminSeededAsync();
        var anon = factory.CreateClient();
        var users = Db.EntityMaintenance.GetTableName<Struo.Infrastructure.Identity.User>();
        Capture.Clear();
        var r = await GetAsync(anon, "/api/items/language?filter[id][_in]=" + Q($"0 OR (SELECT COUNT(*) FROM {users}) > 0"));
        r.Status.Should().Be(HttpStatusCode.BadRequest);
        Capture.Matching(users).Should().BeEmpty();
    }

    [Fact]
    public async Task Stacked_statements_are_refused_and_nothing_is_created()
    {
        var anon = factory.CreateClient();
        const string marker = "struo_qv_marker";
        var r = await GetAsync(anon, "/api/items/language?filter[id][_in]=" + Q($"0; CREATE TABLE {marker} (x int);"));
        r.Status.Should().Be(HttpStatusCode.BadRequest);
        Db.DbMaintenance.IsAnyTable(marker, false).Should().BeFalse();
    }

    [Fact]
    public async Task The_json_query_body_refuses_a_mixed_array()
    {
        var anon = factory.CreateClient();
        foreach (var body in new[]
                 {
                     """{"filter":{"id":{"_in":["0 OR 1=1"]}}}""",
                     """{"filter":{"id":{"_in":[1,"0) OR (1=1"]}}}""",
                     """{"filter":{"id":{"_nin":[1,null]}}}""",
                     """{"filter":{"id":{"_in":[[1]]}}}""",
                 })
        {
            var r = await SendAsync(anon, HttpMethod.Post, "/api/items/language/query", body);
            r.Status.Should().Be(HttpStatusCode.BadRequest, body);
            ErrorCode(r.Root).Should().Be("BAD_USER_INPUT");
        }
    }

    [Fact]
    public async Task Graphql_refuses_a_value_that_is_not_of_the_column_type()
    {
        var anon = factory.CreateClient();
        foreach (var query in new[]
                 {
                     """{ languages(filter: { id: { in: ["0 OR 1=1"] } }) { items { id } } }""",
                     """{ languages(filter: { id: { nin: ["-1) AND (1=0"] } }) { items { id } } }""",
                 })
        {
            Capture.Clear();
            var response = await anon.PostAsJsonAsync("/graphql", new { query });
            var text = await response.Content.ReadAsStringAsync();
            text.Should().Contain("\"errors\"");
            text.Should().NotContain("\"items\":[{");
            NoPayloadInSql("1=1");
            NoPayloadInSql("1=0");
        }
    }

    [Fact]
    public async Task Text_sets_bind_every_value_whatever_the_backend()
    {
        var anon = factory.CreateClient();
        var codes = (await Db.Queryable<Language>().ToListAsync()).Select(l => l.Code).ToList();
        codes.Should().NotBeEmpty();

        var hit = await GetAsync(anon, "/api/items/language?filter[code][_in]=" + Q(codes[0]));
        hit.Status.Should().Be(HttpStatusCode.OK);
        hit.Root.GetProperty("data").EnumerateArray().Select(r => r.GetProperty("code").GetString()).Should().Contain(codes[0]);

        foreach (var payload in new[] { QuoteBreak, SlashBreak })
        {
            foreach (var op in new[] { "_in", "_nin" })
            {
                Capture.Clear();
                var r = await GetAsync(anon, $"/api/items/language?filter[code][{op}]={Q(payload)}");
                r.Status.Should().Be(HttpStatusCode.OK, $"{op} {payload}");
                var count = r.Root.GetProperty("data").GetArrayLength();
                if (op == "_in") count.Should().Be(0);
                else count.Should().Be(codes.Count);
                NoPayloadInSql("OR 1=1");
            }
            var json = await SendAsync(anon, HttpMethod.Post, "/api/items/language/query",
                JsonSerializer.Serialize(new { filter = new { code = new { _in = new[] { codes[0], payload } } } }));
            json.Status.Should().Be(HttpStatusCode.OK);
            json.Root.GetProperty("data").GetArrayLength().Should().Be(1);
        }
    }

    [Fact]
    public async Task Valid_typed_sets_still_work()
    {
        var anon = factory.CreateClient();
        var languages = await Db.Queryable<Language>().ToListAsync();
        var ids = languages.Select(l => l.Id).ToList();

        var byId = await GetAsync(anon, "/api/items/language?filter[id][_in]=" + string.Join(",", ids));
        byId.Status.Should().Be(HttpStatusCode.OK);
        Ids(byId.Root).Should().BeEquivalentTo(ids.Select(i => i.ToString()));

        var without = await GetAsync(anon, $"/api/items/language?filter[id][_nin]={ids[0]}");
        Ids(without.Root).Should().NotContain(ids[0].ToString());

        var bools = await GetAsync(anon, "/api/items/language?filter[enabled][_in]=true,false");
        bools.Status.Should().Be(HttpStatusCode.OK);
        var empty = await GetAsync(anon, "/api/items/language?filter[id][_in]=");
        empty.Root.GetProperty("data").GetArrayLength().Should().Be(0);
    }
}
