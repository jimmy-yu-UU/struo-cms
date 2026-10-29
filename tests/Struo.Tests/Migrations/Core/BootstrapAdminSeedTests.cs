using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Struo.Application.Configuration;
using Struo.Application.Security;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Infrastructure.Migrations.FailAfterSeedProbe;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Core;

[Collection("MigrationCli")] // the CLI tests set a static options override; keep these serial with them
public sealed class BootstrapAdminSeedTests : IDisposable
{
    private readonly CoreMigrationHarness _h = new();

    public BootstrapAdminSeedTests() =>
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Infrastructure.Migrations.Core" };

    public void Dispose()
    {
        MigrationCli.OptionsOverride = o => o;
        _h.Dispose();
    }

    private sealed class CapturingProvider(ListLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Blank_password_with_an_email_carries_a_refusal_and_no_hash(string? blank)
    {
        var seed = CoreMigrationHarness.Seed("a@b.c", blank);

        seed.AdminRefusal.Should().Contain("Auth__BootstrapAdmin__Password");
        seed.AdminPasswordHash.Should().BeNull();
    }

    [Fact]
    public async Task The_seed_migration_throws_the_refusal_on_an_empty_database()
    {
        var host = _h.Host(CoreMigrationHarness.Seed("a@b.c", ""));

        var act = () => host.ApplyAsync(default);

        await act.Should().ThrowAsync<MigrationFailedException>().WithMessage("*Auth__BootstrapAdmin__Password*");
    }

    [Fact]
    public void A_configured_password_is_hashed()
    {
        var seed = CoreMigrationHarness.Seed("a@b.c", "configured-pw");

        seed.HasAdmin.Should().BeTrue();
        new Argon2idPasswordHasher().Verify(seed.AdminPasswordHash!, "configured-pw").Should().BeTrue();
    }

    [Fact]
    public void No_email_means_no_admin_and_no_password_is_needed()
    {
        var seed = CoreMigrationHarness.Seed(email: null, password: "");

        seed.HasAdmin.Should().BeFalse();
    }

    [Fact]
    public void The_hash_is_redacted()
    {
        var seed = CoreMigrationHarness.Seed("a@b.c", "configured-pw");

        seed.Redacted().AdminPasswordHash.Should().Be(CoreSeedData.RedactionPlaceholder);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("short")]
    public void Weak_password_carries_a_refusal(string weak)
    {
        var seed = CoreMigrationHarness.Seed("a@b.c", weak);

        seed.AdminRefusal.Should().Contain("Auth__BootstrapAdmin__Password");
    }

    [Fact]
    public void The_refusal_names_the_configured_minimum_length()
    {
        var seed = CoreSeedData.From(
            new LocalizationOptions(), new Argon2idPasswordHasher(), "a@b.c", "only-ten-ch", [],
            new PasswordPolicyOptions { MinLength = 12 });

        seed.AdminRefusal.Should().Contain("12 characters");
    }

    private const string ConfiguredPassword = "configured-pw-123";

    [Fact]
    public async Task Migrate_logs_no_password_or_hash()
    {
        var capture = new ListLogger();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingProvider(capture)));
        using var sp = Services(factory, password: ConfiguredPassword);

        var first = await Run(sp, "migrate");
        var second = await Run(sp, "migrate");

        first.Code.Should().Be(MigrationCli.Success, first.Err);
        second.Code.Should().Be(MigrationCli.Success, second.Err);
        var hash = _h.Db().Queryable<User>().Single().Password;
        new Argon2idPasswordHasher().Verify(hash, ConfiguredPassword).Should().BeTrue();
        capture.Entries.Select(e => e.Message).Should().NotContain(m => m.Contains(ConfiguredPassword) || m.Contains(hash));
        (first.Out + first.Err + second.Out + second.Err).Should().NotContain(ConfiguredPassword).And.NotContain(hash);
    }

    [Fact]
    public async Task Preview_never_prints_password()
    {
        var capture = new ListLogger();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingProvider(capture)));
        using var sp = Services(factory, password: ConfiguredPassword);

        var preview = await Run(sp, "migrate:preview");
        var status = await Run(sp, "migrate:status");

        preview.Code.Should().Be(MigrationCli.Success, preview.Err);
        preview.Out.Should().Contain("INSERT INTO").And.Contain("<redacted>").And.NotContain("$argon2id");
        var everything = preview.Out + preview.Err + status.Out + status.Err
            + string.Join("\n", capture.Entries.Select(e => e.Message));
        everything.Should().NotContain(ConfiguredPassword);
    }

    [Fact]
    public async Task A_failing_later_migration_logs_no_password()
    {
        var capture = new ListLogger();
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(new CapturingProvider(capture)));
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Infrastructure.Migrations" };
        using var sp = Services(
            factory, FailAfterSeedProbe.TriggerEmail,
            [typeof(StruoMigration).Assembly, typeof(FailAfterSeedProbe).Assembly], ConfiguredPassword);

        var run = await Run(sp, "migrate");

        run.Code.Should().Be(MigrationCli.Failure);
        run.Err.Should().Contain(FailAfterSeedProbe.Message);
        var hash = _h.Db().Queryable<User>().Single().Password;
        capture.Entries.Select(e => e.Message).Should().NotContain(m => m.Contains(ConfiguredPassword) || m.Contains(hash));
    }

    [Theory]
    [InlineData("")]
    [InlineData("admin")]
    public async Task An_already_seeded_database_migrates_when_the_password_is_cleared_or_weak(string later)
    {
        using var first = Services(NullLoggerFactory.Instance, password: ConfiguredPassword);
        (await Run(first, "migrate")).Code.Should().Be(MigrationCli.Success);

        using var again = Services(NullLoggerFactory.Instance, password: later);
        var migrate = await Run(again, "migrate");
        var preview = await Run(again, "migrate:preview");

        migrate.Code.Should().Be(MigrationCli.Success, migrate.Err);
        preview.Code.Should().Be(MigrationCli.Success, preview.Err);
        _h.Db().Queryable<User>().Count().Should().Be(1);
    }

    [Fact]
    public async Task A_weak_bootstrap_password_on_an_empty_database_fails_the_command()
    {
        using var sp = Services(NullLoggerFactory.Instance, password: "short");

        var run = await Run(sp, "migrate");

        run.Code.Should().Be(MigrationCli.Failure);
        run.Err.Should().Contain("Auth__BootstrapAdmin__Password").And.NotContain("   at ");
    }

    [Fact]
    public async Task A_missing_bootstrap_password_fails_the_command_naming_the_setting()
    {
        using var sp = Services(NullLoggerFactory.Instance, "gen@example.com", [typeof(StruoMigration).Assembly], "");

        var run = await Run(sp, "migrate");

        run.Code.Should().Be(MigrationCli.Failure);
        run.Err.Should().Contain("migrate failed:").And.Contain("Auth__BootstrapAdmin__Password").And.NotContain("   at ");
    }

    [Fact]
    public async Task A_refused_bootstrap_password_fails_the_command_without_a_stack_trace()
    {
        using var sp = Services(NullLoggerFactory.Instance, "gen@example.com", [typeof(StruoMigration).Assembly], "admin");

        var run = await Run(sp, "migrate");

        run.Code.Should().Be(MigrationCli.Failure);
        run.Err.Should().Contain("migrate failed:").And.Contain("Auth__BootstrapAdmin__Password").And.NotContain("   at ");
    }

    private ServiceProvider Services(
        ILoggerFactory factory, string email = "gen@example.com",
        Assembly[]? assemblies = null, string password = ConfiguredPassword)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Auth:BootstrapAdmin:Email"] = email,
            ["Auth:BootstrapAdmin:Password"] = password,
        }).Build();
        return new ServiceCollection()
            .AddSingleton(Options.Create(_h.Database.Options(_h.Prefix)))
            .AddSingleton<IConfiguration>(config)
            .AddSingleton(Options.Create(new LocalizationOptions()))
            .AddSingleton(Options.Create(new PasswordPolicyOptions()))
            .AddSingleton<IPasswordHasher, Argon2idPasswordHasher>()
            .AddSingleton(new ScannedAssemblies(assemblies ?? [typeof(StruoMigration).Assembly]))
            .AddSingleton(factory)
            .BuildServiceProvider();
    }

    private static async Task<(int Code, string Out, string Err)> Run(IServiceProvider sp, params string[] args)
    {
        var (o, e) = (new StringWriter(), new StringWriter());
        var code = await MigrationCli.RunAsync(MigrationCli.Parse(args), sp, o, e, default);
        return (code, o.ToString(), e.ToString());
    }
}
