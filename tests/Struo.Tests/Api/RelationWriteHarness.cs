using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Identity;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;

namespace Struo.Tests.Api;

/// <summary>What a write answered: HTTP status, error code and message (null on success) and the body with the trace id removed.</summary>
internal sealed record WriteOutcome(HttpStatusCode Status, string? Code, string? Message, string Body);

/// <summary>Seeds rows and sends item writes over REST or GraphQL for the relation-write permission tests.</summary>
internal sealed partial class RelationWriteHarness(StruoApiFactory factory)
{
    public const string Rest = "REST";
    public const string Gql = "GQL";
    public const string Post = "POST";
    public const string Put = "PUT";
    public const string Existing = "existing";
    public const string Missing = "missing";
    public const string Malformed = "malformed";

    private const string MissingId = "11111111-2222-3333-4444-555555555555";
    private const string MalformedId = "zzz";

    [GeneratedRegex("\"traceId\":\"[^\"]*\"")]
    private static partial Regex TraceId();

    public static readonly string[] IdKinds = [Existing, Missing, Malformed];

    /// <summary>The id text of one kind: the seeded row's id, a well-formed id of no row, or text that is no id.</summary>
    public static string IdOf(string kind, Guid existing) => kind switch
    {
        Existing => existing.ToString(),
        Missing => MissingId,
        _ => MalformedId,
    };

    public async Task<HttpClient> CallerAsync(string[] read, string[] write) =>
        (await factory.CreateEditorClientAsync(read, write)).client;

    private async Task<T> WithDbAsync<T>(Func<ISqlSugarClient, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<ISqlSugarClient>());
    }

    public async Task<Guid> SeedTagAsync() => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxTag { Id = id, Name = "tag" }).ExecuteCommandAsync();
        return id;
    });

    public async Task<Guid> SeedGlossaryAsync() => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxGlossary { Id = id }).ExecuteCommandAsync();
        return id;
    });

    public async Task<Guid> SeedFileAsync() => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new Struo.Infrastructure.Files.File
        {
            Id = id, StorageKey = $"key-{id}", FileName = "a.png", ContentType = "image/png", Size = 1,
        }).ExecuteCommandAsync();
        return id;
    });

    public async Task<Guid> SeedFolderAsync() => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new MediaFolder { Id = id, Name = "folder" }).ExecuteCommandAsync();
        return id;
    });

    public async Task<Guid> SeedPlainRoleAsync() => await WithDbAsync(async db =>
    {
        var role = new Role { Id = Guid.CreateVersion7(), Name = $"plain-{Guid.NewGuid():N}" };
        await db.Insertable(role).ExecuteCommandAsync();
        return role.Id;
    });

    public async Task<Guid> SeedPlainUserAsync() => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new User { Id = id, Email = $"plain-{id:N}@struo.test", Password = "x", IsActive = true })
            .ExecuteCommandAsync();
        return id;
    });

    public Task<int> RoleLinkCountAsync(Guid user) =>
        WithDbAsync(db => db.Queryable<UserRole>().Where(l => l.UserId == user).CountAsync());

    public async Task<Guid> SeedLexiconAsync(Guid? glossary = null) => await WithDbAsync(async db =>
    {
        var id = Guid.CreateVersion7();
        await db.Insertable(new FxLexicon { Id = id, Name = "lexicon", GlossaryId = glossary }).ExecuteCommandAsync();
        return id;
    });

    /// <summary>An article created through the API by a super-admin, optionally linked to tags.</summary>
    public async Task<Guid> SeedArticleAsync(HttpClient admin, params Guid[] tags)
    {
        var resp = await admin.PostAsJsonAsync("/api/items/fxArticle", new
        {
            tags,
            translations = new { en = new { title = "seed" } },
        });
        resp.EnsureSuccessStatusCode();
        var root = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(root.GetProperty("data").GetProperty("id").GetString()!);
    }

    public Task<int> ArticleCountAsync() => WithDbAsync(db => db.Queryable<FxArticle>().CountAsync());
    public Task<int> LexiconCountAsync() => WithDbAsync(db => db.Queryable<FxLexicon>().CountAsync());

    public Task<int> LinkCountAsync(Guid article) =>
        WithDbAsync(db => db.Queryable<FxArticleTag>().Where(l => l.ArticleId == article).CountAsync());

    public Task<int> LinksToTagAsync(Guid tag) =>
        WithDbAsync(db => db.Queryable<FxArticleTag>().Where(l => l.TagId == tag).CountAsync());

    public Task<FxArticle> ArticleAsync(Guid id) =>
        WithDbAsync(db => db.Queryable<FxArticle>().Where(a => a.Id == id).FirstAsync());

    public Task<FxLexicon> LexiconAsync(Guid id) =>
        WithDbAsync(db => db.Queryable<FxLexicon>().Where(l => l.Id == id).FirstAsync());

    public Task<Struo.Infrastructure.Files.File> FileAsync(Guid id) =>
        WithDbAsync(db => db.Queryable<Struo.Infrastructure.Files.File>().Where(f => f.Id == id).FirstAsync());

    private static string Pascal(string collection) => char.ToUpperInvariant(collection[0]) + collection[1..];

    /// <summary>
    /// Sends one create (<c>id</c> null) or update. A create of a translated collection carries a default-locale
    /// translation. <paramref name="fields"/> are the item's body keys, spelled as REST and GraphQL both take them.
    /// </summary>
    public async Task<WriteOutcome> SendAsync(
        HttpClient client, string channel, string collection, Guid? id, Dictionary<string, object?> fields,
        bool translated = false)
    {
        var body = new Dictionary<string, object?>(fields);
        if (channel == Rest)
        {
            if (translated && id is null) body["translations"] = new { en = new { title = "x" } };
            var resp = id is null
                ? await client.PostAsJsonAsync($"/api/items/{collection}", body)
                : await client.PutAsJsonAsync($"/api/items/{collection}/{id}", body);
            return await OutcomeAsync(resp, restEnvelope: true);
        }

        if (translated && id is null)
            body["translations"] = new[] { new { locale = "en", fields = new { title = "x" } } };
        var type = Pascal(collection) + (id is null ? "CreateInput" : "UpdateInput");
        var query = id is null
            ? $"mutation($i: {type}!) {{ create{Pascal(collection)}(input: $i) {{ id }} }}"
            : $"mutation($i: {type}!) {{ update{Pascal(collection)}(id: \"{id}\", input: $i) {{ id }} }}";
        var gql = await client.PostAsJsonAsync("/graphql", new { query, variables = new { i = body } });
        return await OutcomeAsync(gql, restEnvelope: false);
    }

    private static JsonElement? ErrorOf(JsonElement root, bool restEnvelope)
    {
        if (restEnvelope) return root.TryGetProperty("error", out var error) ? error : null;
        return root.TryGetProperty("errors", out var errors) ? errors[0] : null;
    }

    private static async Task<WriteOutcome> OutcomeAsync(HttpResponseMessage resp, bool restEnvelope)
    {
        var raw = TraceId().Replace(await resp.Content.ReadAsStringAsync(), "\"traceId\":\"-\"");
        var root = JsonDocument.Parse(raw).RootElement;
        if (ErrorOf(root, restEnvelope) is not { } error) return new WriteOutcome(resp.StatusCode, null, null, raw);
        var code = restEnvelope
            ? error.GetProperty("code").GetString()
            : error.GetProperty("extensions").GetProperty("code").GetString();
        return new WriteOutcome(resp.StatusCode, code, error.GetProperty("message").GetString(), raw);
    }
}
