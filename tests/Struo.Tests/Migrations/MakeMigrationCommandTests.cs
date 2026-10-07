using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Infrastructure.Migrations.Commands;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Migrations.Schema;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations
{
    [Collection("MigrationCli")] // MigrationCli.Clock is static; keep these tests serial
    public sealed class MakeMigrationCommandTests : IDisposable
    {
        private static readonly FakeClock Clock = new(new DateTimeOffset(2026, 10, 7, 3, 4, 0, TimeSpan.Zero));
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "make-migration-" + Guid.NewGuid().ToString("N"));

        public MakeMigrationCommandTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            MigrationCli.Clock = TimeProvider.System;
            foreach (var sp in _providers) sp.Dispose();
            Directory.Delete(_dir, true);
        }

        private readonly List<ServiceProvider> _providers = [];

        private sealed class FakeClock(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        private sealed class StubCollector(params Type[] types) : IEntityTypeCollector
        {
            public IReadOnlyList<Type> CollectForInitTables() => types;
        }

        // A unique table prefix: SqlSugar caches entity info per prefix for the whole process.
        private ServiceProvider Services(params Type[] types) => Track(
            new ServiceCollection()
                .AddSingleton<IEntityTypeCollector>(new StubCollector(types))
                .AddSingleton(Options.Create(new DatabaseOptions
                    { DbType = StruoDbType.Sqlite, ConnectionString = "Data Source=:memory:", TablePrefix = "mkmig_" }))
                .AddScoped<ISqlSugarClient>(sp => SqlSugarClientFactory.Create(
                    sp.GetRequiredService<IOptions<DatabaseOptions>>().Value, new TestCurrentUserAccessor(Guid.Empty)))
                .BuildServiceProvider());

        private ServiceProvider Track(ServiceProvider sp)
        {
            _providers.Add(sp);
            return sp;
        }

        private ServiceProvider DefaultServices() =>
            Services(typeof(SchemaProbe), typeof(Revision));

        private async Task<(int Code, string Out, string Err)> Run(IServiceProvider sp, params string[] args)
        {
            var (o, e) = (new StringWriter(), new StringWriter());
            var code = await MakeMigrationCommand.RunAsync(args, sp, o, e, Clock);
            return (code, o.ToString(), e.ToString());
        }

        private string Path_(string name) => Path.Combine(_dir, name);

        [Fact]
        public async Task Without_an_entity_it_writes_an_empty_skeleton()
        {
            var (code, stdout, _) = await Run(DefaultServices(), "CreateThing", "--output", _dir);

            code.Should().Be(0);
            var path = Path_("202610070304_CreateThing.cs");
            stdout.Trim().Should().Be($"Created {path}");
            var text = await File.ReadAllTextAsync(path);
            text.Should().Contain("[Migration(202610070304, \"CreateThing\")]")
                .And.Contain("public sealed class CreateThing : StruoMigration");
            text.Should().NotContain("Create.Table").And.NotContain("\r");
            (await File.ReadAllBytesAsync(path)).Take(3).Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF });
        }

        [Fact]
        public async Task Entity_by_simple_name_generates_the_table()
        {
            var (code, _, _) = await Run(DefaultServices(), "CreateRevisions", "--entity", "Revision", "--output", _dir);

            code.Should().Be(0);
            var text = await File.ReadAllTextAsync(Path_("202610070304_CreateRevisions.cs"));
            text.Should().Contain("FrameworkTable(\"revisions\")");
        }

        [Fact]
        public async Task Entity_by_full_name_and_namespace_option()
        {
            var (code, _, _) = await Run(DefaultServices(),
                "CreateProbe", "--entity", typeof(SchemaProbe).FullName!, "--output", _dir, "--namespace", "Acme.Migrations");

            code.Should().Be(0);
            var text = await File.ReadAllTextAsync(Path_("202610070304_CreateProbe.cs"));
            text.Should().Contain("namespace Acme.Migrations;").And.Contain("\"schema_probe\"");
        }

        [Fact]
        public async Task Options_accept_the_equals_form()
        {
            var (code, _, _) = await Run(DefaultServices(), "CreateEq", "--entity=Revision", $"--output={_dir}");

            code.Should().Be(0);
            File.Exists(Path_("202610070304_CreateEq.cs")).Should().BeTrue();
        }

        [Fact]
        public async Task Unknown_entity_is_a_usage_error()
        {
            var (code, stdout, err) = await Run(DefaultServices(), "CreateX", "--entity", "Nope", "--output", _dir);

            code.Should().Be(2);
            stdout.Should().BeEmpty();
            err.Should().Contain("No entity named 'Nope'.").And.Contain("make:migration <Name>");
            Directory.GetFiles(_dir).Should().BeEmpty();
        }

        [Fact]
        public async Task Ambiguous_simple_name_lists_the_full_names_sorted()
        {
            var sp = Services(typeof(Schema.B.Widget), typeof(Schema.A.Widget));

            var (code, stdout, err) = await Run(sp, "CreateW", "--entity", "Widget", "--output", _dir);

            code.Should().Be(2);
            stdout.Should().BeEmpty();
            err.Should().Contain("make:migration <Name>");
            Directory.GetFiles(_dir).Should().BeEmpty();
            var a = err.IndexOf("Struo.Tests.Migrations.Schema.A.Widget", StringComparison.Ordinal);
            var b = err.IndexOf("Struo.Tests.Migrations.Schema.B.Widget", StringComparison.Ordinal);
            a.Should().BeGreaterThan(-1);
            b.Should().BeGreaterThan(a);
        }

        [Fact]
        public async Task Missing_output_is_a_usage_error()
        {
            var (code, stdout, err) = await Run(DefaultServices(), "CreateX");

            code.Should().Be(2);
            stdout.Should().BeEmpty();
            err.Should().Contain("--output").And.Contain("make:migration <Name>");
        }

        [Fact]
        public async Task Nonexistent_output_directory_is_a_usage_error()
        {
            var (code, _, err) = await Run(DefaultServices(), "CreateX", "--output", Path_("missing"));

            code.Should().Be(2);
            err.Should().Contain("does not exist");
        }

        [Theory]
        [InlineData("1Bad")]
        [InlineData("class")]
        public async Task Invalid_class_name_is_a_usage_error(string name)
        {
            var (code, _, err) = await Run(DefaultServices(), name, "--output", _dir);

            code.Should().Be(2);
            err.Should().Contain(name);
            Directory.GetFiles(_dir).Should().BeEmpty();
        }

        [Fact]
        public async Task Invalid_namespace_is_a_usage_error()
        {
            var (code, _, err) = await Run(DefaultServices(), "CreateX", "--output", _dir, "--namespace", "Bad..Ns");

            code.Should().Be(2);
            err.Should().Contain("namespace");
        }

        [Fact]
        public async Task Malformed_command_lines_are_usage_errors()
        {
            foreach (var args in new[]
            {
                new[] { "--output", _dir },
                ["CreateX", "--output", _dir, "--bogus", "1"],
                ["CreateX", "--output"],
                ["CreateX", "--output", _dir, "--output", _dir],
                ["CreateX", "Extra", "--output", _dir],
            })
            {
                var (code, stdout, err) = await Run(DefaultServices(), args);
                code.Should().Be(2, string.Join(' ', args));
                stdout.Should().BeEmpty();
                err.Should().Contain("make:migration <Name>");
            }
        }

        [Fact]
        public async Task An_empty_option_value_is_a_missing_value()
        {
            var (code, _, err) = await Run(DefaultServices(), "CreateX", "--output=");

            code.Should().Be(2);
            err.Should().Contain("Option '--output' needs a value.");
        }

        [Fact]
        public async Task An_existing_file_is_not_overwritten()
        {
            var path = Path_("202610070304_CreateThing.cs");
            await File.WriteAllTextAsync(path, "keep me");

            var (code, stdout, err) = await Run(DefaultServices(), "CreateThing", "--output", _dir);

            code.Should().Be(1);
            stdout.Should().BeEmpty();
            err.Trim().Should().Be($"File already exists: {path}");
            (await File.ReadAllTextAsync(path)).Should().Be("keep me");
        }

        [Fact]
        public async Task A_target_that_cannot_be_opened_for_writing_is_one_line()
        {
            var path = Path_("202610070304_CreateThing.cs");
            Directory.CreateDirectory(path);

            var (code, stdout, err) = await Run(DefaultServices(), "CreateThing", "--output", _dir);

            code.Should().Be(1);
            stdout.Should().BeEmpty();
            err.TrimEnd().Should().StartWith($"Could not write {path}: ").And.NotContain("   at ");
            err.TrimEnd().Split('\n').Should().HaveCount(1);
            Directory.Exists(path).Should().BeTrue();
        }

        [Fact]
        public async Task A_column_without_a_portable_mapping_fails_with_the_generator_message()
        {
            var (code, _, err) = await Run(Services(typeof(Unmappable)), "CreateU", "--entity", "Unmappable", "--output", _dir);

            code.Should().Be(1);
            err.Should().Contain("has no portable mapping");
            Directory.GetFiles(_dir).Should().BeEmpty();
        }

        [Fact]
        public async Task It_runs_through_MigrationCli_without_a_database_host()
        {
            MigrationCli.Clock = Clock;
            var (o, e) = (new StringWriter(), new StringWriter());

            var code = await MigrationCli.RunAsync(
                MigrationCli.Parse(["make:migration", "CreateCli", "--output", _dir]), DefaultServices(), o, e, default);

            code.Should().Be(0, e.ToString());
            File.Exists(Path_("202610070304_CreateCli.cs")).Should().BeTrue();
        }
    }

    [SugarTable("unmappable")]
    public sealed class Unmappable
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public TimeSpan Span { get; set; }
    }
}

namespace Struo.Tests.Migrations.Schema.A
{
    public sealed class Widget { public long Id { get; set; } }
}

namespace Struo.Tests.Migrations.Schema.B
{
    public sealed class Widget { public long Id { get; set; } }
}
