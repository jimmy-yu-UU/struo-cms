// tests/Struo.Tests/Api/CoreWriteInterceptorTests.cs
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
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Api;

/// <summary>
/// The core collections' write rules (<c>file</c> status, <c>user</c> session revocation) run as
/// registered interceptors and a delete handler on the item write path, through the real host.
/// </summary>
[Collection("ApiIntegration")]
public class CoreWriteInterceptorTests(ApiFactory factory)
{
    private sealed class ThrowingRevocationService : IUserSessionRevocationService
    {
        public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default) =>
            throw new InvalidOperationException("session index unreachable");
    }

    private sealed class CountingRevocationService : IUserSessionRevocationService
    {
        public readonly List<Guid> Revoked = [];

        public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
        {
            lock (Revoked) Revoked.Add(userId);
            return Task.CompletedTask;
        }
    }

    private async Task<HttpClient> AdminOnAsync(WebApplicationFactory<Program> host)
    {
        await factory.EnsureAdminSeededAsync();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(CsrfProtectionMiddleware.HeaderName, "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        return client;
    }

    private async Task<Guid> SeedUserAsync()
    {
        var userId = Guid.CreateVersion7();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        await db.Insertable(new User
        {
            Id = userId, Email = $"core-{userId:N}@struo.test", Password = hasher.Hash("core-pw-123"), IsActive = true,
        }).ExecuteCommandAsync();
        return userId;
    }

    private async Task<Guid> SeedFileAsync()
    {
        var id = Guid.CreateVersion7();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        await db.Insertable(new File
        {
            Id = id, StorageKey = "k-" + id.ToString("N"), FileName = "a.txt", ContentType = "text/plain", Size = 1,
            Status = "published",
        }).ExecuteCommandAsync();
        return id;
    }

    [Fact]
    public async Task A_failing_session_revocation_after_a_user_delete_is_a_distinct_error_and_the_delete_stays()
    {
        var userId = await SeedUserAsync();
        var admin = await AdminOnAsync(factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddScoped<IUserSessionRevocationService, ThrowingRevocationService>())));

        var resp = await admin.DeleteAsync($"/api/items/user/{userId}");

        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var body = await resp.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("SESSION_REVOCATION_FAILED");
        body.Should().NotContain("session index unreachable");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        (await db.Queryable<User>().In(userId).AnyAsync()).Should().BeFalse("the delete is committed");
    }

    [Fact]
    public async Task Sessions_are_revoked_after_a_user_delete_with_and_without_the_purge_flag_but_not_for_an_unknown_id()
    {
        var plain = await SeedUserAsync();
        var flagged = await SeedUserAsync();
        var counting = new CountingRevocationService();
        var admin = await AdminOnAsync(factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<IUserSessionRevocationService>(counting))));

        (await admin.DeleteAsync($"/api/items/user/{plain}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        counting.Revoked.Should().Equal(plain);

        (await admin.DeleteAsync($"/api/items/user/{flagged}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        counting.Revoked.Should().Equal(plain, flagged);

        (await admin.DeleteAsync($"/api/items/user/{Guid.NewGuid()}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        counting.Revoked.Should().HaveCount(2, "an unknown user revokes nothing");
    }

    [Fact]
    public async Task A_file_update_with_a_status_other_than_published_or_draft_is_rejected()
    {
        var id = await SeedFileAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();

        var bad = await admin.PutAsJsonAsync($"/api/items/file/{id}", new { status = "archived" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await bad.Content.ReadAsStringAsync()).Should().Contain("must be 'published' or 'draft'");

        (await admin.PutAsJsonAsync($"/api/items/file/{id}", new { status = "draft" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("/api/items/file/not-a-guid")]
    [InlineData("/api/items/file/not-a-guid?purge=true")]
    public async Task Rest_delete_of_a_non_guid_file_id_is_404(string url)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        (await admin.DeleteAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GraphQl_delete_of_a_non_guid_file_id_reports_false()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        var resp = await admin.PostAsJsonAsync("/graphql", new
        {
            query = "mutation { deleteFile(id: \"not-a-guid\", purge: true) }"
        });

        var body = await resp.Content.ReadAsStringAsync();
        JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("deleteFile").GetBoolean()
            .Should().BeFalse(body);
    }
}
