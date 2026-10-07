using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public class MigrationGeneratorTests
{
    private const string UpdateEnvVar = "UPDATE_MIGRATION_GOLDEN";
    private const string GeneratedNamespace = "Struo.Tests.Migrations.Schema.Generated";

    private static ISqlSugarClient Client(string prefix = "struo_", TranslationSidecarIndexPolicy? policy = null) =>
        SqlSugarClientFactory.Create(
            new DatabaseOptions
            {
                DbType = StruoDbType.Sqlite,
                ConnectionString = "Data Source=:memory:",
                TablePrefix = prefix
            },
            new TestCurrentUserAccessor(Guid.Empty),
            policy);

    private static ExpectedTable Probe() => EntitySchemaReader.Read(Client(), typeof(SchemaProbe));

    private static ExpectedTable RevisionTable() => EntitySchemaReader.Read(Client(), typeof(Revision));

    private static ExpectedTable FileTranslationTable()
    {
        var policy = new TranslationSidecarIndexPolicy(new Dictionary<Type, TranslationSidecarKey>
        {
            [typeof(FileTranslation)] = TranslationSidecarIndexPolicy.KeyFor(typeof(FileTranslation), "FileId", "Locale")
        });
        return EntitySchemaReader.Read(Client(policy: policy), typeof(FileTranslation));
    }

    public static TheoryData<string, MigrationSpec> GoldenCases() => new()
    {
        { "EmptySkeleton", new MigrationSpec("AddNothing", 202610070900, null, null) },
        { "EmptySkeletonNamespaced", new MigrationSpec("AddNothing", 202610070900, "Acme.Migrations", null) },
        { "SchemaProbe", new MigrationSpec("CreateSchemaProbe", 202610070901, "Acme.Migrations", Probe()) },
        { "Revision", new MigrationSpec("CreateRevisions", 202610070902, "Acme.Migrations", RevisionTable()) },
        { "FileTranslation", new MigrationSpec("CreateFileTranslations", 202610070903, "Acme.Migrations", FileTranslationTable()) },
    };

    [Theory]
    [MemberData(nameof(GoldenCases))]
    public void Generate_matches_the_golden_file(string name, MigrationSpec spec) =>
        CompareOrWrite(MigrationGenerator.Generate(spec), Path.Combine(SchemaTestsDir(), "Golden", name + ".cs.txt"));

    public static TheoryData<string, MigrationSpec> GeneratedCases() => new()
    {
        { "SchemaProbe", new MigrationSpec("CreateSchemaProbe", 202610071001, $"{GeneratedNamespace}.SchemaProbe", Probe()) },
        { "Revision", new MigrationSpec("CreateRevisions", 202610071002, $"{GeneratedNamespace}.Revision", RevisionTable()) },
        { "FileTranslation", new MigrationSpec("CreateFileTranslations", 202610071003, $"{GeneratedNamespace}.FileTranslation", FileTranslationTable()) },
    };

    [Theory]
    [MemberData(nameof(GeneratedCases))]
    public void Checked_in_generated_migration_matches_the_generator(string name, MigrationSpec spec) =>
        CompareOrWrite(MigrationGenerator.Generate(spec), Path.Combine(SchemaTestsDir(), "Generated", name + ".cs"));

    [Fact]
    public void Empty_skeleton_has_empty_up_and_down()
    {
        var source = MigrationGenerator.Generate(new MigrationSpec("AddNothing", 1, null, null));

        source.Should().Contain("public override void Up()").And.Contain("public override void Down()");
        source.Should().NotContain("Create.").And.NotContain("Delete.").And.NotContain("namespace");
    }

    [Fact]
    public void Other_category_cannot_be_generated()
    {
        var table = Probe() with
        {
            Columns = [new ExpectedColumn("weird", ColumnCategory.Other, false, null, false, false, false)]
        };

        var act = () => MigrationGenerator.Generate(new MigrationSpec("CreateWeird", 1, null, table));

        act.Should().Throw<NotSupportedException>().WithMessage("Column 'weird' has no portable mapping");
    }

    [Fact]
    public void An_explicit_column_data_type_is_refused_naming_the_column()
    {
        var table = EntitySchemaReader.Read(Client("ex_"), typeof(ExplicitTypeProbe));

        var act = () => MigrationGenerator.Generate(new MigrationSpec("CreateExplicit", 1, null, table));

        act.Should().Throw<NotSupportedException>().WithMessage("*'wide'*");
    }

    [Fact]
    public void Decimal_with_zero_digits_emits_scale_zero()
    {
        var table = EntitySchemaReader.Read(Client("ex_"), typeof(ExplicitTypeProbe));
        table = table with { Columns = table.Columns.Where(c => c.Name != "wide").ToList() };

        MigrationGenerator.Generate(new MigrationSpec("CreateWhole", 1, null, table))
            .Should().Contain(".WithColumn(\"whole\").AsDecimal(10, 0)");
    }

    [Fact]
    public void FileName_is_version_and_class_name() =>
        MigrationGenerator.FileName(new MigrationSpec("AddNothing", 202610070900, null, null))
            .Should().Be("202610070900_AddNothing.cs");

    [Theory]
    [InlineData("CreatePosts", true)]
    [InlineData("_Private1", true)]
    [InlineData("", false)]
    [InlineData("1Create", false)]
    [InlineData("Create Posts", false)]
    [InlineData("Create-Posts", false)]
    [InlineData("class", false)]
    [InlineData("namespace", false)]
    [InlineData("foo", false)]
    [InlineData("_foo1", false)]
    [InlineData("StruoMigration", false)]
    public void IsValidClassName_accepts_identifiers_only(string name, bool expected) =>
        MigrationGenerator.IsValidClassName(name).Should().Be(expected);

    [Theory]
    [InlineData("Acme", true)]
    [InlineData("Acme.Data.Migrations", true)]
    [InlineData("_a.b1", true)]
    [InlineData("", false)]
    [InlineData("Acme.", false)]
    [InlineData(".Acme", false)]
    [InlineData("Acme..Data", false)]
    [InlineData("Acme.class", false)]
    [InlineData("1Acme", false)]
    [InlineData("Acme.Da ta", false)]
    public void IsValidNamespace_requires_identifier_segments(string ns, bool expected) =>
        MigrationGenerator.IsValidNamespace(ns).Should().Be(expected);

    private static ExpectedColumn Column(string name, ColumnCategory category, int? length = null, int? scale = null) =>
        new(name, category, false, length, false, false, false, scale);

    private static string GenerateFor(ExpectedTable table) =>
        MigrationGenerator.Generate(new MigrationSpec("CreateT", 1, null, table));

    private static ExpectedTable TableOf(bool framework, ExpectedColumn[] columns, ExpectedIndex[] indexes) =>
        new(typeof(object), framework ? "struo_t" : "t", "t", framework, columns, indexes, null);

    [Fact]
    public void Decimal_with_precision_and_scale_emits_both_and_double_emits_as_double()
    {
        var source = GenerateFor(TableOf(false,
        [
            Column("a", ColumnCategory.Decimal, 10, 2),
            Column("b", ColumnCategory.Decimal),
            Column("c", ColumnCategory.Decimal, 12),
            Column("d", ColumnCategory.Double)
        ], []));

        source.Should().Contain(".WithColumn(\"a\").AsDecimal(10, 2)")
            .And.Contain(".WithColumn(\"b\").AsDecimal()")
            .And.Contain(".WithColumn(\"c\").AsDecimal()")
            .And.Contain(".WithColumn(\"d\").AsDouble()");
    }

    [Fact]
    public void Braces_in_a_framework_index_name_are_escaped_in_the_interpolated_string()
    {
        var source = GenerateFor(TableOf(true, [Column("a", ColumnCategory.Integer)],
            [new ExpectedIndex("ux_{x}_struo_t_a}", ["a"], false)]));

        source.Should().Contain("$\"ux_{{x}}_{FrameworkTable(\"t\")}_a}}\"");
    }

    [Fact]
    public void Control_characters_in_names_are_escaped()
    {
        var source = GenerateFor(TableOf(false, [Column("a\nb\tc\rd", ColumnCategory.Integer)], []));

        source.Should().Contain(".WithColumn(\"a\\nb\\tc\\rd\")");
    }

    private static string SchemaTestsDir() =>
        Path.Combine(RepoRoot.Find(), "tests", "Struo.Tests", "Migrations", "Schema");

    private static void CompareOrWrite(string actual, string path)
    {
        var relative = Path.GetRelativePath(RepoRoot.Find(), path).Replace('\\', '/');
        var expectedText = actual.Replace("\r\n", "\n").TrimEnd('\n') + "\n";

        if (Environment.GetEnvironmentVariable(UpdateEnvVar) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, expectedText);
            return;
        }

        System.IO.File.Exists(path).Should().BeTrue(
            $"{relative} is missing. Regenerate it with `{UpdateEnvVar}=1 dotnet test --filter MigrationGenerator` and commit the result.");

        System.IO.File.ReadAllText(path).Replace("\r\n", "\n").Should().Be(expectedText,
            $"the generator output no longer matches {relative}. If the change is intended, regenerate with " +
            $"`{UpdateEnvVar}=1 dotnet test --filter MigrationGenerator` and commit it.");
    }
}
