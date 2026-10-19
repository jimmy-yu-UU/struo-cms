using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Identity;

[Collection("ApiIntegration")]
public class EmailNormalizationTests(ApiFactory factory)
{
    private const string Password = "pw12345678";
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}";

    private async Task<User> LoadByIdAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return await db.Queryable<User>().Where(u => u.Id == id).FirstAsync();
    }

    private static async Task<Guid> IdOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(body).RootElement;
        return (root.TryGetProperty("data", out var data) ? data : root).GetProperty("id").GetGuid();
    }

    [Theory]
    [InlineData("  Alice@Example.com ", "alice@example.com")]
    [InlineData("ALICE@EXAMPLE.COM", "alice@example.com")]
    public void Normalize_trims_and_lowercases(string raw, string expected) =>
        EmailAddress.Normalize(raw).Should().Be(expected);

    [Fact]
    public async Task Rest_create_stores_lowercase_and_rejects_case_variant_with_409()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var local = Unique("Alice");
        var first = await admin.PostAsJsonAsync("/api/users", new { email = $"{local}@Example.com", password = Password });
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await IdOfAsync(first);
        (await LoadByIdAsync(id)).Email.Should().Be($"{local.ToLowerInvariant()}@example.com");

        var second = await admin.PostAsJsonAsync("/api/users",
            new { email = $"{local.ToUpperInvariant()}@example.com", password = Password });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().Contain("CONFLICT");
    }

    [Fact]
    public async Task Generic_items_create_of_a_case_variant_returns_409()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var local = Unique("Gen");
        (await admin.PostAsJsonAsync("/api/users", new { email = $"{local}@X.com", password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var variant = await admin.PostAsJsonAsync("/api/items/user", new { email = $"{local.ToUpperInvariant()}@x.COM" });
        variant.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await variant.Content.ReadAsStringAsync()).Should().Contain("CONFLICT");
    }

    [Fact]
    public async Task Generic_items_update_to_case_variant_of_another_users_email_returns_409()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var taken = $"{Unique("taken")}@x.com";
        (await admin.PostAsJsonAsync("/api/users", new { email = taken, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var other = await admin.PostAsJsonAsync("/api/users", new { email = $"{Unique("other")}@x.com", password = Password });
        var otherId = await IdOfAsync(other);

        var put = await admin.PutAsJsonAsync($"/api/items/user/{otherId}", new { email = taken.ToUpperInvariant() });
        put.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Generic_items_update_lowercases_and_allows_a_user_to_keep_its_own_email()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var local = Unique("self");
        var created = await admin.PostAsJsonAsync("/api/users", new { email = $"{local}@x.com", password = Password });
        var id = await IdOfAsync(created);

        var same = await admin.PutAsJsonAsync($"/api/items/user/{id}", new { email = $"{local.ToUpperInvariant()}@X.COM" });
        same.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoadByIdAsync(id)).Email.Should().Be($"{local}@x.com");
    }

    [Fact]
    public async Task Login_matches_any_casing_and_still_matches_a_preexisting_mixed_case_row()
    {
        var email = $"{Unique("login")}@x.com";
        var admin = await factory.CreateAuthenticatedClientAsync();
        (await admin.PostAsJsonAsync("/api/users", new { email, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = email.ToUpperInvariant(), password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var legacy = $"Legacy{Guid.NewGuid():N}@Mixed.com";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            await db.Insertable(new User
            {
                Id = Guid.CreateVersion7(), Email = legacy, Password = hasher.Hash(Password), IsActive = true,
            }).ExecuteCommandAsync();
        }
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = legacy.ToLowerInvariant(), password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email = legacy, password = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Oidc_auto_provision_stores_lowercase()
    {
        using var file = TestBackend.CreateUnitDatabase();
        var db = SqlSugarClientFactory.Create(file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<User>();
        var store = new SqlSugarExternalUserStore(db);

        var id = await store.CreateExternalUserAsync("Bob@X.com", "Bob");

        (await db.Queryable<User>().Where(u => u.Id == id).FirstAsync()).Email.Should().Be("bob@x.com");
    }

    [Fact]
    public void Bootstrap_seed_admin_email_is_lowercased()
    {
        var seed = CoreSeedData.From(new LocalizationOptions(), new Argon2idPasswordHasher(),
            " Admin@Example.COM ", "s3cret-pw-long-enough", []);
        seed.AdminEmail.Should().Be("admin@example.com");
    }
}
