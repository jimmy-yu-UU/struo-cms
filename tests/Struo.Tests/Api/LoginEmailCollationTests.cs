using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// The database matches an email under its collation, which on some backends folds spellings that
/// normalize to different strings. Login and external login accept a found account only when its email is
/// the presented one after normalizing, so a collation-equivalent variant neither reaches the account nor
/// gets a login-lock counter of its own. The duplicate check on user create keeps the collation match.
/// Each backend folds a different set of spellings; <see cref="Variants"/> lists the ones the selected
/// backend folds. The class discriminates only on MySQL, MariaDB and SQL Server. On SQLite and PostgreSQL
/// the list holds one spelling the database does not fold, so the variant is just another email and the
/// tests pass whether or not the normalized comparison exists. The comparison rule itself is covered on
/// every backend by <c>AuthServiceTests</c> and <c>ExternalLoginServiceTests</c>.
/// </summary>
[Collection("ApiIntegration")]
public class LoginEmailCollationTests(ApiFactory factory)
{
    private const string Password = "collation-pw-123456";

    private sealed record Variant(string Name, Func<string, string> Make, bool FoldedByBackend);

    private static readonly Variant AccentE = new("accent-e", s => s.Replace('e', '\u00E9'), true);
    private static readonly Variant UmlautO = new("umlaut-o", s => s.Replace('o', '\u00F6'), true);
    private static readonly Variant FullwidthO = new("fullwidth-o", s => s.Replace('o', '\uFF4F'), true);
    private static readonly Variant SoftHyphen = new("soft-hyphen", s => s.Insert(1, "\u00AD"), true);
    private static readonly Variant ZeroWidthSpace = new("zero-width-space", s => s.Insert(1, "\u200B"), true);
    private static readonly Variant DotlessI = new("dotless-i", s => s.Replace('i', '\u0131'), true);
    private static readonly Variant DottedCapitalI = new("dotted-capital-i", s => s.Replace('i', '\u0130'), true);
    private static readonly Variant SharpS = new("sharp-s", s => s.Replace('s', '\u00DF'), true);

    private static IReadOnlyList<Variant> Variants => TestBackend.Current switch
    {
        TestBackendKind.MySql => [AccentE, UmlautO, DotlessI, DottedCapitalI, SharpS],
        TestBackendKind.MariaDb => [AccentE, UmlautO, FullwidthO, SoftHyphen, ZeroWidthSpace, DottedCapitalI],
        TestBackendKind.SqlServer => [FullwidthO],
        _ => [AccentE with { FoldedByBackend = false }],
    };

    private async Task<(Guid Id, string Email)> SeedAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var id = Guid.CreateVersion7();
        var email = EmailAddress.Normalize($"josie-{id:N}@struo.test");
        await db.Insertable(new User
        {
            Id = id, Email = email, Password = hasher.Hash(Password), Name = "Collation", IsActive = true
        }).ExecuteCommandAsync();
        return (id, email);
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    private static async Task<(HttpStatusCode Status, string Code, string Message)> ErrorOf(HttpResponseMessage r)
    {
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        var error = doc.RootElement.GetProperty("error");
        return (r.StatusCode, error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!);
    }

    private WebApplicationFactory<Program> LockingHost() =>
        factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["RateLimiting:LoginAccount:Enabled"] = "true",
                ["RateLimiting:LoginAccount:PermitLimit"] = "3",
                ["RateLimiting:LoginAccount:WindowSeconds"] = "300",
                ["RateLimiting:Login:Enabled"] = "false",
            })));

    [Fact]
    public async Task Password_login_with_the_exact_email_succeeds()
    {
        var (_, email) = await SeedAsync();

        (await LoginAsync(factory.CreateClient(), $" {email.ToUpperInvariant()} ", Password)).StatusCode
            .Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Password_login_with_a_variant_gets_the_unknown_account_response()
    {
        var client = factory.CreateClient();
        var unknown = await ErrorOf(await LoginAsync(client, $"nobody-{Guid.NewGuid():N}@struo.test", Password));

        foreach (var variant in Variants)
        {
            var (_, email) = await SeedAsync();
            var response = await LoginAsync(client, variant.Make(email), Password);

            (await ErrorOf(response)).Should().Be(unknown, variant.Name);
        }
    }

    [Fact]
    public async Task A_variant_gets_no_login_lock_counter_of_its_own()
    {
        using var host = LockingHost();
        var client = host.CreateClient();

        foreach (var variant in Variants)
        {
            var (_, email) = await SeedAsync();
            for (var i = 0; i < 3; i++)
                await LoginAsync(client, email, "wrong-pw-xxxx");

            (await LoginAsync(client, email, Password)).StatusCode
                .Should().Be(HttpStatusCode.TooManyRequests, variant.Name);
            (await LoginAsync(client, variant.Make(email), Password)).StatusCode
                .Should().NotBe(HttpStatusCode.OK, variant.Name);
        }
    }

    [Fact]
    public async Task External_login_with_a_variant_is_refused_and_creates_no_account()
    {
        foreach (var variant in Variants)
        {
            var (id, email) = await SeedAsync();
            using var scope = factory.Services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IExternalLoginService>();
            var open = new ExternalLoginPolicy(false, null, []);

            var result = await svc.ResolveOrProvisionAsync(
                new ExternalIdentity("iss", null, variant.Make(email), null, "V"), open);

            if (variant.FoldedByBackend)
            {
                result.Succeeded.Should().BeFalse(variant.Name);
                result.Failure.Should().Be(ExternalLoginFailure.AccountMismatch, variant.Name);
                var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                var rows = await db.Queryable<User>().Where(u => u.Email == email).ToListAsync();
                rows.Select(r => r.Id).Should().Equal(id);
            }
            else
            {
                result.Succeeded.Should().BeTrue(variant.Name);
                result.Provisioned.Should().BeTrue(variant.Name);
            }
        }
    }

    [Fact]
    public async Task External_login_with_the_exact_email_links_the_existing_account()
    {
        var (id, email) = await SeedAsync();
        using var scope = factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExternalLoginService>();

        var result = await svc.ResolveOrProvisionAsync(
            new ExternalIdentity("iss", null, $" {email.ToUpperInvariant()}", null, "E"),
            new ExternalLoginPolicy(false, null, []));

        result.UserId.Should().Be(id);
        result.Provisioned.Should().BeFalse();
    }

    [Fact]
    public async Task Creating_a_user_with_a_variant_keeps_the_collation_duplicate_check()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();

        foreach (var variant in Variants)
        {
            var (_, email) = await SeedAsync();
            var response = await admin.PostAsJsonAsync("/api/users",
                new { email = variant.Make(email), password = "collation-pw-123456!x", name = "dup" });

            response.StatusCode.Should().Be(
                variant.FoldedByBackend ? HttpStatusCode.Conflict : HttpStatusCode.Created, variant.Name);
        }
    }
}
