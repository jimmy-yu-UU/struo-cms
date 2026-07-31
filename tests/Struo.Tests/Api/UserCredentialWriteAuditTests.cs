// tests/Struo.Tests/Api/UserCredentialWriteAuditTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The three credential-mutating endpoints on <c>UsersController</c> (change password, issue access
/// token, revoke access token) write via SqlSugar's column-expression form
/// (<c>Updateable&lt;User&gt;().SetColumns(...)</c>). That form is NOT <c>UpdateByObject</c>, so
/// <see cref="Struo.Infrastructure.Persistence.AuditAop"/> — which only hooks
/// <c>InsertByObject</c>/<c>UpdateByObject</c> — never fires for them, and they do not go through
/// <c>SqlSugarItemRepository.UpdateGenericAsync</c>'s version bump either.
/// <para>
/// Two consequences these tests pin down: (1) a credential change left no <c>UpdatedAt</c>/
/// <c>UpdatedBy</c> trace on the row, so there was no record of who changed a password; and (2) the
/// row's <c>Version</c> did not advance, so the optimistic-concurrency guarantee silently did not
/// cover credential changes — an editor holding a pre-change <c>version</c> could still PUT
/// successfully afterwards instead of getting the 409 every other write path produces.
/// </para>
/// </summary>
[Collection("ApiIntegration")]
public class UserCredentialWriteAuditTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    private sealed record RowState(DateTime UpdatedAt, Guid? UpdatedBy, long Version);

    private async Task<RowState> LoadAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var u = await db.Queryable<User>().Where(x => x.Id == userId).FirstAsync();
        u.Should().NotBeNull();
        return new RowState(u!.UpdatedAt, u.UpdatedBy, u.Version);
    }

    /// <summary>Seeds a throwaway user directly (no roles needed — the admin client acts on them).</summary>
    private async Task<Guid> SeedTargetUserAsync(string password)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var id = Guid.CreateVersion7();
        await db.Insertable(new User
        {
            Id = id,
            Email = $"cred-audit-{id:N}@struo.test",
            Password = hasher.Hash(password),
            Name = "Credential Audit Target",
            IsActive = true,
        }).ExecuteCommandAsync();
        return id;
    }

    [Fact]
    public async Task Admin_password_change_stamps_audit_fields_and_bumps_version()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var targetId = await SeedTargetUserAsync("initial-pw-123456");
        var before = await LoadAsync(targetId);

        var response = await admin.PutAsJsonAsync(
            $"/api/users/{targetId}/password", new { newPassword = "changed-pw-123456" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await LoadAsync(targetId);
        after.Version.Should().Be(before.Version + 1, "a credential change must advance the optimistic-lock token");
        after.UpdatedAt.Should().BeAfter(before.UpdatedAt, "the change must be timestamped");
        after.UpdatedBy.Should().Be(_factory.AdminUserId, "the acting admin must be recorded");
    }

    [Fact]
    public async Task Self_service_password_change_records_the_user_as_the_actor()
    {
        const string password = "self-serve-pw-123456";
        var (client, userId) = await _factory.CreateEditorClientAsync([], []);
        var before = await LoadAsync(userId);

        // Self-service requires proving knowledge of the current password (CreateEditorClientAsync's).
        var response = await client.PutAsJsonAsync($"/api/users/{userId}/password",
            new { newPassword = password, currentPassword = "editor-pw-123" });

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = await LoadAsync(userId);
        after.Version.Should().Be(before.Version + 1);
        after.UpdatedBy.Should().Be(userId, "a self-service change is attributable to the user themselves");
    }

    [Fact]
    public async Task Access_token_issue_and_revoke_both_stamp_audit_fields_and_bump_version()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var targetId = await SeedTargetUserAsync("token-target-pw-123456");
        var before = await LoadAsync(targetId);

        var issued = await admin.PostAsync($"/api/users/{targetId}/access-token", null);
        issued.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterIssue = await LoadAsync(targetId);
        afterIssue.Version.Should().Be(before.Version + 1, "issuing a token is a credential change");
        afterIssue.UpdatedBy.Should().Be(_factory.AdminUserId);

        var revoked = await admin.DeleteAsync($"/api/users/{targetId}/access-token");
        revoked.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRevoke = await LoadAsync(targetId);
        afterRevoke.Version.Should().Be(afterIssue.Version + 1, "revoking a token is a credential change too");
        afterRevoke.UpdatedBy.Should().Be(_factory.AdminUserId);
    }

    [Fact]
    public async Task Version_bumped_by_a_password_change_makes_a_stale_item_write_conflict()
    {
        // The end-to-end point of the version bump: after an out-of-band credential change, a client
        // still holding the pre-change version must be told to reload rather than silently winning.
        var admin = await _factory.CreateAuthenticatedClientAsync();
        var targetId = await SeedTargetUserAsync("stale-write-pw-123456");

        var read = await admin.GetAsync($"/api/items/user/{targetId}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var data = doc.RootElement.GetProperty("data");
        var staleVersion = data.GetProperty("version").GetInt64();
        // Email is Required and ItemDeserializer validates Required against the INCOMING payload (not
        // the merged result), so it has to be echoed back or the PUT 400s before reaching the version
        // CAS — which would make this test pass for the wrong reason.
        var email = data.GetProperty("email").GetString();

        (await admin.PutAsJsonAsync($"/api/users/{targetId}/password", new { newPassword = "rotated-pw-123456" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var stalePut = await admin.PutAsJsonAsync($"/api/items/user/{targetId}",
            new { email, name = "Renamed From A Stale Read", version = staleVersion });

        stalePut.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var errorDoc = JsonDocument.Parse(await stalePut.Content.ReadAsStringAsync());
        errorDoc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("VERSION_CONFLICT");
    }
}
