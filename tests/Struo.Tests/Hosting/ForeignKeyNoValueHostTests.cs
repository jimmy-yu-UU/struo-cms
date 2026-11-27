using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Domain.Metadata.Enums;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>
/// A key that cannot hold null uses the default value of its type for "no link", and an empty string is no id:
/// neither is looked up in the target. A nullable number key keeps <c>0</c> as a real id. Stored values are read
/// through row types that map the tables directly. Runs on the backend <c>STRUO_TEST_BACKEND</c> selects.
/// </summary>
[Collection("ApiIntegration")]
public sealed class ForeignKeyNoValueHostTests(ForeignKeyNoValueHostTests.HostFixture fixture)
    : IClassFixture<ForeignKeyNoValueHostTests.HostFixture>
{
    private const string Uuid = "nvUuidOwner";
    private const string Int = "nvIntOwner";
    private const string Long = "nvLongOwner";
    private const string Text = "nvTextOwner";
    private const string NullableInt = "nvIntNullOwner";

    private const string EmptyGuid = "00000000-0000-0000-0000-000000000000";
    private const string ExistingGuid = "00000000-0000-0000-0000-000000000005";
    private const string OwnerId = "ownerId";

    [SugarTable("nvUuidOwner")]
    public sealed class UuidRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public Guid OwnerId { get; set; } }

    [SugarTable("nvIntOwner")]
    public sealed class IntRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public int OwnerId { get; set; } }

    [SugarTable("nvLongOwner")]
    public sealed class LongRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public long OwnerId { get; set; } }

    [SugarTable("nvTextOwner")]
    public sealed class TextRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public string? OwnerId { get; set; } }

    [SugarTable("nvIntNullOwner")]
    public sealed class NullableIntRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public int? OwnerId { get; set; } }

    [SugarTable("nvUuidTarget")]
    public sealed class UuidTargetRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public string Name { get; set; } = ""; }

    [SugarTable("nvIntTarget")]
    public sealed class IntTargetRow { [SugarColumn(IsPrimaryKey = true)] public int Id { get; set; } public string Name { get; set; } = ""; }

    [SugarTable("nvLongTarget")]
    public sealed class LongTargetRow { [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; } public string Name { get; set; } = ""; }

    [SugarTable("nvTextTarget")]
    public sealed class TextTargetRow { [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; } = ""; public string Name { get; set; } = ""; }

    private static readonly EmittedProperty Name = new("Name", typeof(string), FieldInterface.Text);

    private static EmittedEntity Target(string name, Type? idType) => new(name, [Name], IdType: idType);

    private static EmittedEntity Owner(string name, string target, Type keyType, FieldInterface face, bool nullable) => new(
        name, [Name, new EmittedProperty("OwnerId", keyType, face, Nullable: nullable)],
        Relations: [new EmittedRelation("Owner", target, "OwnerId")]);

    public sealed class HostFixture : IAsyncLifetime
    {
        public ForkHost Host { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            (Host, Client) = await EmittedGraphQlHost.StartAsync(
                [
                    Target("NvUuidTarget", null), Target("NvIntTarget", typeof(int)),
                    Target("NvLongTarget", typeof(long)), Target("NvTextTarget", typeof(string)),
                    Owner("NvUuidOwner", "NvUuidTarget", typeof(Guid), FieldInterface.Uuid, false),
                    Owner("NvIntOwner", "NvIntTarget", typeof(int), FieldInterface.Number, false),
                    Owner("NvLongOwner", "NvLongTarget", typeof(long), FieldInterface.Number, false),
                    Owner("NvTextOwner", "NvTextTarget", typeof(string), FieldInterface.Text, true),
                    Owner("NvIntNullOwner", "NvIntTarget", typeof(int?), FieldInterface.Number, true),
                ],
                "nvhost");
            await SeedTargetsAsync();
        }

        private async Task SeedTargetsAsync()
        {
            using var scope = Host.App.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            await db.Insertable(new UuidTargetRow { Id = Guid.Parse(ExistingGuid), Name = "t" }).ExecuteCommandAsync();
            await db.Insertable(new IntTargetRow { Id = 5, Name = "t" }).ExecuteCommandAsync();
            await db.Insertable(new LongTargetRow { Id = 5, Name = "t" }).ExecuteCommandAsync();
            await db.Insertable(new TextTargetRow { Id = "five", Name = "t" }).ExecuteCommandAsync();
        }

        public async Task DisposeAsync() => await Host.DisposeAsync();
    }

    private async Task<(HttpStatusCode Status, string Text)> SendAsync(HttpMethod method, string url, JsonObject body)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        using var response = await fixture.Client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private async Task<object?> StoredAsync(string collection, string id)
    {
        using var scope = fixture.Host.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var guid = Guid.Parse(id);
        return collection switch
        {
            Uuid => (await db.Queryable<UuidRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId,
            Int => (await db.Queryable<IntRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId,
            Long => (await db.Queryable<LongRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId,
            Text => (await db.Queryable<TextRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId,
            _ => (await db.Queryable<NullableIntRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId,
        };
    }

    private static string Tag() => "t" + Guid.NewGuid().ToString("N");

    // A collection, the "no value" spelling of its key, and the value the table then holds.
    public static TheoryData<string, string, object> NoValues() => new()
    {
        { Uuid, $"\"{EmptyGuid}\"", Guid.Empty },
        { Int, "0", 0 },
        { Long, "0", 0L },
        { Text, "\"\"", "" },
    };

    // The key of a row of the collection's target, and the value the table then holds.
    private static (JsonNode Key, object Stored) Existing(string collection) => collection switch
    {
        Uuid => (ExistingGuid, Guid.Parse(ExistingGuid)),
        Int or NullableInt => (5, 5),
        Long => (5L, 5L),
        _ => ("five", "five"),
    };

    private async Task<string> CreateAsync(string collection)
    {
        var body = new JsonObject { ["name"] = Tag(), [OwnerId] = Existing(collection).Key.DeepClone() };
        var (status, text) = await SendAsync(HttpMethod.Post, $"/api/items/{collection}", body);
        status.Should().Be(HttpStatusCode.Created, text);
        return JsonNode.Parse(text)!["data"]!["id"]!.GetValue<string>();
    }

    [Theory]
    [MemberData(nameof(NoValues))]
    public async Task A_create_with_the_no_value_spelling_is_accepted_and_stores_it(string collection, string json, object expected)
    {
        var body = new JsonObject { ["name"] = Tag(), [OwnerId] = JsonNode.Parse(json) };

        var (status, text) = await SendAsync(HttpMethod.Post, $"/api/items/{collection}", body);

        status.Should().Be(HttpStatusCode.Created, text);
        (await StoredAsync(collection, JsonNode.Parse(text)!["data"]!["id"]!.GetValue<string>())).Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(NoValues))]
    public async Task An_update_with_the_no_value_spelling_is_accepted_and_stores_it(string collection, string json, object expected)
    {
        var id = await CreateAsync(collection);

        var (status, text) = await SendAsync(
            HttpMethod.Put, $"/api/items/{collection}/{id}", new JsonObject { [OwnerId] = JsonNode.Parse(json) });

        status.Should().Be(HttpStatusCode.OK, text);
        (await StoredAsync(collection, id)).Should().Be(expected);
    }

    [Fact]
    public async Task A_nullable_number_key_keeps_zero_as_a_real_id()
    {
        var id = await CreateAsync(NullableInt);

        var (created, createdText) = await SendAsync(
            HttpMethod.Post, $"/api/items/{NullableInt}", new JsonObject { ["name"] = Tag(), [OwnerId] = 0 });
        var (updated, updatedText) = await SendAsync(
            HttpMethod.Put, $"/api/items/{NullableInt}/{id}", new JsonObject { [OwnerId] = 0 });

        created.Should().Be(HttpStatusCode.BadRequest, createdText);
        updated.Should().Be(HttpStatusCode.BadRequest, updatedText);
        createdText.Should().Contain("The id in 'ownerId' does not exist in 'nvIntTarget'.");
        (await StoredAsync(NullableInt, id)).Should().Be(5);
    }

    [Theory]
    [InlineData(Int, "7")]
    [InlineData(Long, "7")]
    [InlineData(Text, "\"nope\"")]
    [InlineData(Text, "\" \"")]
    public async Task A_value_that_names_no_row_is_still_refused(string collection, string json)
    {
        var id = await CreateAsync(collection);

        var (status, text) = await SendAsync(
            HttpMethod.Put, $"/api/items/{collection}/{id}", new JsonObject { [OwnerId] = JsonNode.Parse(json) });

        status.Should().Be(HttpStatusCode.BadRequest, text);
        (await StoredAsync(collection, id)).Should().Be(Existing(collection).Stored);
    }
}
