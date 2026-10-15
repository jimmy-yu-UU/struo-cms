using System.Reflection;
using System.Text.RegularExpressions;
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
    public void Blank_password_generates_a_strong_one(string? blank)
    {
        var hasher = new Argon2idPasswordHasher();

        var seed = CoreMigrationHarness.Seed("a@b.c", blank, hasher: hasher);

        seed.HasAdmin.Should().BeTrue();
        seed.GeneratedPassword.Should().NotBeNull().And.HaveLength(24);
        seed.GeneratedPassword.Should().MatchRegex("^[A-Za-z0-9]{24}$");
        hasher.Verify(seed.AdminPasswordHash!, seed.GeneratedPassword!).Should().BeTrue();
    }

    [Fact]
    public void A_configured_password_is_hashed_and_nothing_is_generated()
    {
        var seed = CoreMigrationHarness.Seed("a@b.c", "configured-pw");

        seed.GeneratedPassword.Should().BeNull();
        new Argon2idPasswordHasher().Verify(seed.AdminPasswordHash!, "configured-pw").Should().BeTrue();
    }

    [Fact]
    public void No_email_means_no_admin_and_no_generated_password()
    {
        var seed = CoreMigrationHarness.Seed(email: null, password: "");

        seed.HasAdmin.Should().BeFalse();
        seed.GeneratedPassword.Should().BeNull();
    }

    [Fact]
    public void Generated_password_is_redacted()
    {
        var seed = CoreMigrationHarness.Seed("a@b.c", "");

        var redacted = seed.Redacted();

        redacted.GeneratedPassword.Should().BeNull();
        redacted.AdminPasswordHash.Should().Be(CoreSeedData.RedactionPlaceholder);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ADMIN")]
    [InlineData("short")]
    public void Weak_password_is_refused(string weak)
    {
        var act = () => CoreMigrationHarness.Seed("a@b.c", weak);

        act.Should().Throw<BootstrapPasswordRefusedException>().WithMessage("*Auth__BootstrapAdmin__Password*");
    }

    [Fact]
    public void The_refusal_names_the_configured_minimum_length()
    {
        var act = () => CoreSeedData.From(
            new LocalizationOptions(), new Argon2idPasswordHasher(), "a@b.c", "only-ten-ch", [],
            new PasswordPolicyOptions { MinLength = 12 });

        act.Should().Throw<BootstrapPasswordRefusedException>().WithMessage("*12 characters*");
    }

    [Fact]
    public async Task Migrate_prints_generated_password_once()
    {
        var capture = new ListLogger();
        var provider = new CapturingProvider(capture);
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        using var sp = Services(factory);

        var first = await Run(sp, "migrate");
        var second = await Run(sp, "migrate");

        first.Code.Should().Be(MigrationCli.Success, first.Err);
        second.Code.Should().Be(MigrationCli.Success, second.Err);
        var hash = _h.Db().Queryable<User>().Single().Password;
        var warnings = capture.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        var line = warnings.Should().ContainSingle(m => m.Contains("gen@example.com")).Subject;
        var password = Regex.Match(line, "password ([A-Za-z0-9]{24})").Groups[1].Value;
        password.Should().HaveLength(24);
        new Argon2idPasswordHasher().Verify(hash, password).Should().BeTrue();
        capture.Entries.Count(e => e.Message.Contains(password)).Should().Be(1);
        (first.Out + first.Err + second.Out + second.Err).Should().NotContain(password).And.NotContain(hash);
    }

    [Fact]
    public async Task Preview_never_prints_password()
    {
        var capture = new ListLogger();
        var provider = new CapturingProvider(capture);
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        using var sp = Services(factory);

        var preview = await Run(sp, "migrate:preview");
        var status = await Run(sp, "migrate:status");

        preview.Code.Should().Be(MigrationCli.Success, preview.Err);
        preview.Out.Should().Contain("INSERT INTO").And.Contain("<redacted>").And.NotContain("$argon2id");
        var everything = preview.Out + preview.Err + status.Out + status.Err
            + string.Join("\n", capture.Entries.Select(e => e.Message));
        Regex.IsMatch(everything, "password [A-Za-z0-9]{24}").Should().BeFalse();
        everything.Should().NotContain("generated password");
    }

    [Fact]
    public async Task A_failing_later_migration_still_reports_the_generated_password()
    {
        var capture = new ListLogger();
        var provider = new CapturingProvider(capture);
        using var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        MigrationCli.OptionsOverride = o => o with { NamespaceFilter = "Struo.Infrastructure.Migrations" };
        using var sp = Services(factory, FailAfterSeedProbe.TriggerEmail, [typeof(StruoMigration).Assembly, typeof(FailAfterSeedProbe).Assembly]);

        var run = await Run(sp, "migrate");

        run.Code.Should().Be(MigrationCli.Failure);
        run.Err.Should().Contain(FailAfterSeedProbe.Message);
        var hash = _h.Db().Queryable<User>().Single().Password;
        var line = capture.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message)
            .Should().ContainSingle(m => m.Contains(FailAfterSeedProbe.TriggerEmail)).Subject;
        var password = Regex.Match(line, "password ([A-Za-z0-9]{24})").Groups[1].Value;
        new Argon2idPasswordHasher().Verify(hash, password).Should().BeTrue();
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
        Assembly[]? assemblies = null, string password = "")
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
