using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Core;
using Xunit;

namespace Struo.Tests.Migrations.Core;

public sealed class CoreSeedMigrationTests : IDisposable
{
    private readonly CoreMigrationHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Languages_follow_the_shipped_default_with_default_flag_and_sort()
    {
        await _h.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        var rows = _h.Db().Queryable<Language>().OrderBy(l => l.Sort).ToList();

        rows.Select(r => (r.Code, r.Name, r.IsDefault, r.Enabled, r.Sort)).Should().Equal(
            ("en", "English", true, true, 1),
            ("zh-TW", LocalizationOptions.ShippedDefault[1].Name, false, true, 2));
        rows.Should().OnlyContain(r => r.CreatedAt > DateTime.UtcNow.AddMinutes(-5) && r.UpdatedAt == r.CreatedAt);
    }

    [Fact]
    public async Task Roles_are_seeded_with_the_seeders_values()
    {
        await _h.Host(CoreMigrationHarness.Seed()).ApplyAsync(default);

        var roles = _h.Db().Queryable<Role>().ToList().ToDictionary(r => r.Name);

        roles.Keys.Should().BeEquivalentTo(["admin", "public"]);
        roles["admin"].IsSuperAdmin.Should().BeTrue();
        roles["admin"].Description.Should().Be("Full access");
        roles["public"].IsSuperAdmin.Should().BeFalse();
        roles["public"].Description.Should().Be("Anonymous callers");
        roles.Values.Should().OnlyContain(r => r.Id != Guid.Empty && r.Version == 0 && r.CreatedBy == null);
    }

    [Fact]
    public async Task Admin_user_has_a_verifiable_password_and_the_admin_role()
    {
        var hasher = new Argon2idPasswordHasher();
        await _h.Host(CoreMigrationHarness.Seed(hasher: hasher)).ApplyAsync(default);
        var db = _h.Db();

        var user = db.Queryable<User>().Single();
        var adminRole = db.Queryable<Role>().Single(r => r.Name == "admin");

        user.Email.Should().Be("admin@example.com");
        user.Name.Should().Be("Administrator");
        user.IsActive.Should().BeTrue();
        user.AccessToken.Should().BeNull();
        user.Password.Should().NotBe("s3cret-pw");
        hasher.Verify(user.Password, "s3cret-pw").Should().BeTrue();
        var link = db.Queryable<UserRole>().Single();
        (link.UserId, link.RoleId).Should().Be((user.Id, adminRole.Id));
    }

    [Theory]
    [InlineData(null, "pw")]
    [InlineData("", "pw")]
    [InlineData("  ", "pw")]
    [InlineData("admin@example.com", null)]
    [InlineData("admin@example.com", " ")]
    public async Task Blank_email_or_password_seeds_no_user_and_no_user_role(string? email, string? password)
    {
        await _h.Host(CoreMigrationHarness.Seed(email, password)).ApplyAsync(default);
        var db = _h.Db();

        db.Queryable<User>().Count().Should().Be(0);
        db.Queryable<UserRole>().Count().Should().Be(0);
        db.Queryable<Role>().Count().Should().Be(2);
        _h.Host(null).GetStatus().Should().OnlyContain(m => m.State == MigrationState.Applied);
    }

    [Fact]
    public async Task Public_read_collections_become_one_read_only_permission_each_for_the_public_role()
    {
        await _h.Host(CoreMigrationHarness.Seed(publicRead: ["article", "tag", "article"])).ApplyAsync(default);
        var db = _h.Db();

        var publicRole = db.Queryable<Role>().Single(r => r.Name == "public");
        var perms = db.Queryable<Permission>().ToList();

        perms.Select(p => p.Collection).Should().BeEquivalentTo(["article", "tag"]);
        perms.Should().OnlyContain(p => p.RoleId == publicRole.Id && p.CanRead && !p.CanWrite && !p.CanDelete && p.Version == 0);
    }

    [Fact]
    public async Task Running_again_applies_nothing_and_adds_no_rows()
    {
        var seed = CoreMigrationHarness.Seed(publicRead: ["article"]);
        await _h.Host(seed).ApplyAsync(default);

        var second = await _h.Host(seed).ApplyAsync(default);

        second.Should().BeEmpty();
        var db = _h.Db();
        (db.Queryable<Language>().Count(), db.Queryable<Role>().Count(), db.Queryable<User>().Count(),
            db.Queryable<UserRole>().Count(), db.Queryable<Permission>().Count()).Should().Be((2, 2, 1, 1, 1));
    }

    [Fact]
    public async Task Missing_seed_data_fails_with_a_clear_message()
    {
        var act = () => _h.Host(null).ApplyAsync(default);

        (await act.Should().ThrowAsync<MigrationFailedException>())
            .Which.Message.Should().Contain("needs seed data").And.Contain("MigrationHostOptions.Seed");
    }

    [Fact]
    public void Preview_writes_nothing_and_never_shows_the_plain_password()
    {
        var sql = new StringWriter();

        var pending = _h.Host(CoreMigrationHarness.Seed(password: "plain-pw-9f3")).Preview(sql);

        pending.Select(m => m.Version).Should().Equal(CreateCoreSchema.Version, SeedCoreData.Version);
        sql.ToString().Should().Contain("INSERT INTO").And.Contain("$argon2").And.NotContain("plain-pw-9f3");
        _h.Db().DbMaintenance.GetTableInfoList(false).Should().BeEmpty();
    }

    [Fact]
    public void From_marks_the_default_language_case_insensitively_and_numbers_sort_from_one()
    {
        var localization = new LocalizationOptions
        {
            DefaultLanguage = "FR",
            Languages = [new() { Code = "en", Name = "English" }, new() { Code = "fr", Name = "Francais" }],
        };

        var seed = CoreSeedData.From(localization, new Argon2idPasswordHasher(), null, null, []);

        seed.Languages.Should().Equal(new SeedLanguage("en", "English", false, 1), new SeedLanguage("fr", "Francais", true, 2));
        (seed.AdminEmail, seed.AdminPasswordHash).Should().Be(((string?)null, (string?)null));
    }

    [Fact]
    public void FromServices_reads_localization_and_the_config_keys_and_hashes_the_password()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:BootstrapAdmin:Email"] = "root@example.com",
            ["Auth:BootstrapAdmin:Password"] = "pw-1",
            ["Rbac:PublicReadCollections:0"] = "article",
        }).Build();
        using var sp = new ServiceCollection()
            .AddSingleton<IConfiguration>(config)
            .AddSingleton(Options.Create(new LocalizationOptions()))
            .AddSingleton<IPasswordHasher, Argon2idPasswordHasher>()
            .BuildServiceProvider();

        var seed = CoreSeedData.FromServices(sp);

        seed.AdminEmail.Should().Be("root@example.com");
        new Argon2idPasswordHasher().Verify(seed.AdminPasswordHash!, "pw-1").Should().BeTrue();
        seed.PublicReadCollections.Should().Equal("article");
        seed.Languages.Should().HaveCount(2);
    }
}
