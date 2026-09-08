// tests/Struo.Tests/Api/ItemChangeListenerApiTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Changes;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// Proves IItemChangeListener fires end-to-end over both REST and GraphQL, on a derived
// WebApplicationFactory host with multiple listeners registered side by side (the scoped
// IItemChangeNotifier resolves every singleton IItemChangeListener registered on the derived
// host's service collection). No listener is registered on the base ApiFactory host used by the
// rest of the suite, so this is entirely opt-in per test via WithWebHostBuilder.
[Collection("ApiIntegration")]
public class ItemChangeListenerApiTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private async Task<(HttpClient Client, RecordingItemChangeListener A, RecordingItemChangeListener B)> HostAsync()
    {
        var a = new RecordingItemChangeListener();
        var b = new RecordingItemChangeListener();
        var client = _factory.WithWebHostBuilder(h => h.ConfigureServices(s =>
        {
            s.AddSingleton<IItemChangeListener>(a);
            s.AddSingleton<IItemChangeListener>(b);
        })).CreateClient();
        await _factory.EnsureAdminSeededAsync();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        return (client, a, b);
    }

    private static async Task<string> IdOf(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task Rest_create_trash_and_purge_reach_every_registered_listener_once_each()
    {
        var (client, a, b) = await HostAsync();

        var created = await client.PostAsJsonAsync("/api/items/category",
            new { name = "u5b-" + Guid.NewGuid().ToString("N")[..8] });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = await IdOf(created);
        foreach (var l in new[] { a, b })
            l.Calls.Should().ContainSingle()
                .Which.Should().ContainSingle()
                .Which.Should().Be(new ItemChange("category", id, ItemChangeKind.Created));

        a.Calls.Clear();
        b.Calls.Clear();
        (await client.DeleteAsync($"/api/items/category/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        a.Calls.Should().ContainSingle();
        b.Calls.Should().ContainSingle();
        a.All.Should().ContainSingle().Which.Kind.Should().Be(ItemChangeKind.Trashed);
        b.All.Should().ContainSingle().Which.Kind.Should().Be(ItemChangeKind.Trashed);

        a.Calls.Clear();
        b.Calls.Clear();
        (await client.DeleteAsync($"/api/items/category/{id}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        a.Calls.Should().ContainSingle();
        b.Calls.Should().ContainSingle();
        a.All.Should().ContainSingle().Which.Should().Be(new ItemChange("category", id, ItemChangeKind.Purged));
        b.All.Should().ContainSingle().Which.Should().Be(new ItemChange("category", id, ItemChangeKind.Purged));
    }

    [Fact]
    public async Task Graphql_mutations_raise_the_same_changes()
    {
        var (client, a, b) = await HostAsync();

        var created = await client.PostAsJsonAsync("/api/items/category",
            new { name = "u5b-gql-" + Guid.NewGuid().ToString("N")[..8] });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = await IdOf(created);
        a.Calls.Clear();
        b.Calls.Clear();

        var r = await client.PostAsJsonAsync("/graphql",
            new { query = $$"""mutation { deleteCategory(id: "{{id}}") }""" });
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        var root = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
        root.TryGetProperty("errors", out _).Should().BeFalse(root.GetRawText());

        a.Calls.Should().ContainSingle();
        b.Calls.Should().ContainSingle();
        a.All.Should().ContainSingle().Which.Should().Be(new ItemChange("category", id, ItemChangeKind.Trashed));
        b.All.Should().ContainSingle().Which.Should().Be(new ItemChange("category", id, ItemChangeKind.Trashed));
    }

    // Proves DI actually injects the notifier into FileService: it is registered in AddStruoFiles(),
    // a DIFFERENT extension method from AddStruoData() (which registers IItemChangeNotifier itself),
    // so a wiring regression there would not be caught by the generic-item tests above.
    [Fact]
    public async Task Rest_file_upload_reaches_every_registered_listener_once()
    {
        var (client, a, b) = await HostAsync();

        // application/octet-stream is not in FileStorageOptions.AllowedContentTypes (appsettings.json);
        // text/plain is, and matches the multipart pattern other Files tests already use.
        var content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new MediaTypeHeaderValue("text/plain") } };
        using var form = new MultipartFormDataContent { { content, "file", "u5b.txt" } };
        var uploaded = await client.PostAsync("/api/files", form);
        uploaded.StatusCode.Should().Be(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
        var id = await IdOf(uploaded);

        foreach (var l in new[] { a, b })
            l.Calls.Should().ContainSingle()
                .Which.Should().ContainSingle()
                .Which.Should().Be(new ItemChange("file", id, ItemChangeKind.Created));
    }

    [Fact]
    public async Task A_listener_that_throws_does_not_change_the_response()
    {
        var bad = new RecordingItemChangeListener { OnCall = _ => throw new InvalidOperationException("index down") };
        var client = _factory.WithWebHostBuilder(h => h.ConfigureServices(s =>
            s.AddSingleton<IItemChangeListener>(bad))).CreateClient();
        await _factory.EnsureAdminSeededAsync();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();

        var created = await client.PostAsJsonAsync("/api/items/category",
            new { name = "u5b-bad-" + Guid.NewGuid().ToString("N")[..8] });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        bad.Calls.Should().ContainSingle();
    }
}
