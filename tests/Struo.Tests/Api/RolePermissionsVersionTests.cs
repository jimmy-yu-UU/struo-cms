using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Api.Auth;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The grants save is guarded by the role row's own <c>Version</c>: <c>?version=N</c> must match,
/// success moves the version on by one and reports it as <c>meta.version</c>.
/// </summary>
[Collection("ApiIntegration")]
public class RolePermissionsVersionTests(ApiFactory factory)
{
    private static readonly object[] ArticleRead =
        [new { collection = "fxArticle", canRead = true, canWrite = false, canDelete = false }];

    private static readonly object[] TagWrite =
        [new { collection = "fxTag", canRead = true, canWrite = true, canDelete = false }];

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp) =>
        await resp.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<Guid> CreateRoleAsync(HttpClient admin)
    {
        var resp = await admin.PostAsJsonAsync("/api/items/role", new { name = $"ver-{Guid.NewGuid():N}" });
        return (await ReadJsonAsync(resp)).GetProperty("data").GetProperty("id").GetGuid();
    }

    private static async Task<long> RoleVersionAsync(HttpClient admin, Guid roleId)
    {
        var json = await ReadJsonAsync(await admin.GetAsync($"/api/items/role/{roleId}"));
        return json.GetProperty("data").GetProperty("version").GetInt64();
    }

    private static Task<HttpResponseMessage> PutGrantsAsync(
        HttpClient client, Guid roleId, object[] body, long? version = null) =>
        client.PutAsJsonAsync(
            $"/api/roles/{roleId}/permissions" + (version is null ? "" : $"?version={version}"), body);

    private static async Task<string[]> GrantNamesAsync(HttpClient admin, Guid roleId)
    {
        var json = await ReadJsonAsync(await admin.GetAsync($"/api/roles/{roleId}/permissions"));
        return json.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("collection").GetString()!).Order().ToArray();
    }

    private static async Task AssertVersionConflictAsync(HttpResponseMessage resp)
    {
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await ReadJsonAsync(resp);
        json.GetProperty("error").GetProperty("code").GetString().Should().Be("VERSION_CONFLICT");
    }

    [Fact]
    public async Task Matching_version_saves_and_reports_the_new_version_in_meta()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var before = await RoleVersionAsync(admin, roleId);

        var resp = await PutGrantsAsync(admin, roleId, ArticleRead, before);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(resp);
        json.GetProperty("data").GetArrayLength().Should().Be(1);
        json.GetProperty("meta").GetProperty("version").GetInt64().Should().Be(before + 1);
        (await RoleVersionAsync(admin, roleId)).Should().Be(before + 1);
    }

    [Fact]
    public async Task Stale_version_is_a_409_and_leaves_grants_and_version_unchanged()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var v0 = await RoleVersionAsync(admin, roleId);
        (await PutGrantsAsync(admin, roleId, ArticleRead, v0)).StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertVersionConflictAsync(await PutGrantsAsync(admin, roleId, TagWrite, v0));

        (await GrantNamesAsync(admin, roleId)).Should().Equal("fxArticle");
        (await RoleVersionAsync(admin, roleId)).Should().Be(v0 + 1);
    }

    [Fact]
    public async Task Without_a_version_the_save_succeeds_and_still_advances_the_version()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var before = await RoleVersionAsync(admin, roleId);

        var resp = await PutGrantsAsync(admin, roleId, ArticleRead);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(resp)).GetProperty("meta").GetProperty("version").GetInt64().Should().Be(before + 1);
        (await RoleVersionAsync(admin, roleId)).Should().Be(before + 1);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    public async Task A_version_that_is_not_an_integer_is_INVALID_VERSION(string raw)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);

        var resp = await admin.PutAsJsonAsync($"/api/roles/{roleId}/permissions?version={raw}", ArticleRead);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadJsonAsync(resp)).GetProperty("error").GetProperty("code").GetString()
            .Should().Be("INVALID_VERSION");
        (await GrantNamesAsync(admin, roleId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Role_form_update_with_the_version_from_before_a_grants_save_is_a_409()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var v0 = await RoleVersionAsync(admin, roleId);
        (await PutGrantsAsync(admin, roleId, ArticleRead, v0)).StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await admin.PutAsJsonAsync($"/api/items/role/{roleId}",
            new { name = $"renamed-{Guid.NewGuid():N}", version = v0 });
        await AssertVersionConflictAsync(stale);

        var fresh = await admin.PutAsJsonAsync($"/api/items/role/{roleId}",
            new { name = $"renamed-{Guid.NewGuid():N}", version = v0 + 1 });
        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Grants_save_with_the_version_from_before_a_role_form_save_is_a_409()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var v0 = await RoleVersionAsync(admin, roleId);
        var update = await admin.PutAsJsonAsync($"/api/items/role/{roleId}",
            new { name = $"renamed-{Guid.NewGuid():N}", version = v0 });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        await AssertVersionConflictAsync(await PutGrantsAsync(admin, roleId, ArticleRead, v0));
        (await GrantNamesAsync(admin, roleId)).Should().BeEmpty();

        (await PutGrantsAsync(admin, roleId, ArticleRead, v0 + 1)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unknown_role_is_a_404_even_with_a_version()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        (await PutGrantsAsync(admin, Guid.NewGuid(), ArticleRead, 3)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_caller_without_write_on_roles_gets_403_before_any_version_check()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var (editor, _) = await factory.CreateEditorClientAsync([], []);

        (await PutGrantsAsync(editor, roleId, ArticleRead, 999)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_grants_save_leaves_the_roles_audit_stamps_alone()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var before = (await ReadJsonAsync(await admin.GetAsync($"/api/items/role/{roleId}"))).GetProperty("data");

        (await PutGrantsAsync(admin, roleId, ArticleRead, before.GetProperty("version").GetInt64()))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var after = (await ReadJsonAsync(await admin.GetAsync($"/api/items/role/{roleId}"))).GetProperty("data");
        after.GetProperty("updatedAt").GetString().Should().Be(before.GetProperty("updatedAt").GetString());
        after.GetProperty("updatedBy").ToString().Should().Be(before.GetProperty("updatedBy").ToString());
    }

    // The role is removed after the controller's existence check and before the version update.
    private sealed class DeletingBeforeReplaceStore(IPermissionGrantStore inner, ISqlSugarClient db) : IPermissionGrantStore
    {
        public Task<bool> RoleExistsAsync(Guid roleId, CancellationToken ct = default) => inner.RoleExistsAsync(roleId, ct);

        public Task<IReadOnlyList<PermissionGrant>> ListAsync(Guid roleId, CancellationToken ct = default) =>
            inner.ListAsync(roleId, ct);

        public async Task<long?> ReplaceAsync(
            Guid roleId, IReadOnlyList<PermissionGrant> grants, long? expectedVersion = null,
            CancellationToken ct = default)
        {
            await db.Deleteable<Role>().Where(r => r.Id == roleId).ExecuteCommandAsync(ct);
            return await inner.ReplaceAsync(roleId, grants, expectedVersion, ct);
        }

        public Task DeleteForRoleAsync(Guid roleId, CancellationToken ct = default) => inner.DeleteForRoleAsync(roleId, ct);
    }

    [Fact]
    public async Task A_role_deleted_between_the_check_and_the_save_is_a_404_with_the_not_found_body()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var version = await RoleVersionAsync(admin, roleId);
        var unknown = await ReadJsonAsync(await PutGrantsAsync(admin, Guid.NewGuid(), ArticleRead, version));

        using var host = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddScoped<IPermissionGrantStore>(sp => new DeletingBeforeReplaceStore(
                ActivatorUtilities.CreateInstance<SqlSugarPermissionGrantStore>(sp),
                sp.GetRequiredService<ISqlSugarClient>()))));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();

        var resp = await PutGrantsAsync(client, roleId, ArticleRead, version);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var body = await ReadJsonAsync(resp);
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("NOT_FOUND");
        body.GetProperty("error").GetProperty("message").GetString()
            .Should().Be(unknown.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public async Task Two_parallel_saves_with_the_same_version_yield_one_200_and_one_409()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var roleId = await CreateRoleAsync(admin);
        var v0 = await RoleVersionAsync(admin, roleId);

        var first = PutGrantsAsync(admin, roleId, ArticleRead, v0);
        var second = PutGrantsAsync(admin, roleId, TagWrite, v0);
        var responses = await Task.WhenAll(first, second);

        responses.Select(r => r.StatusCode).Order().Should().Equal(HttpStatusCode.OK, HttpStatusCode.Conflict);
        var winnerIsFirst = responses[0].StatusCode == HttpStatusCode.OK;
        (await GrantNamesAsync(admin, roleId)).Should().Equal(winnerIsFirst ? "fxArticle" : "fxTag");
        (await RoleVersionAsync(admin, roleId)).Should().Be(v0 + 1);
    }
}
