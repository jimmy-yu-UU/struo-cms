using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Attributes;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations.Schema;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class Utf8Mb4DefaultsTests
{
    private const string Prefix = "struo_";
    private const string Database = "web_cms";
    private const string UsersTable = "struo_users";
    private const string FilesTable = "struo_files";
    private const string Latin1Collation = "latin1_swedish_ci";
    private const string Utf8Mb3Collation = "utf8mb3_general_ci";
    private const string Utf8Mb4Collation = "utf8mb4_0900_ai_ci";
    private const string UnicodeCollation = "utf8mb4_unicode_ci";

    private static SchemataRow Schema(string? characterSet, string? collation = null) =>
        new() { SchemaName = Database, DefaultCharacterSetName = characterSet, DefaultCollationName = collation };

    private static TablesRow Table(string name, string? collation) => new() { TableSchema = Database, TableName = name, TableCollation = collation };

    private static CatalogDefaults Catalog(SchemataRow? schema, params TablesRow[] tables) =>
        new(Database, schema is null ? [] : [schema], tables);

    private static string? Refusal(string? databaseSet, params TablesRow[] tables) =>
        Utf8Mb4Defaults.Refusal(Catalog(Schema(databaseSet), tables), Prefix);

    private static string? Refusal(SchemataRow schema, params TablesRow[] tables) =>
        Utf8Mb4Defaults.Refusal(Catalog(schema, tables), Prefix);

    [Theory]
    [InlineData("utf8mb4", "utf8mb4_0900_ai_ci", false)]
    [InlineData("utf8mb4", "utf8mb4_uca1400_ai_ci", false)]
    [InlineData("utf8mb4", "utf8mb4_unicode_ci", false)]
    [InlineData("UTF8MB4", "UTF8MB4_BIN", false)]
    [InlineData("latin1", "utf8mb4_0900_ai_ci", true)]
    [InlineData("utf8mb3", "utf8mb4_0900_ai_ci", true)]
    [InlineData("utf8mb4", "latin1_swedish_ci", true)]
    [InlineData("utf8mb4", "utf8mb3_general_ci", true)]
    [InlineData("utf8mb4", "utf8mb3_uca1400_ai_ci", true)]
    [InlineData("latin1", "latin1_swedish_ci", true)]
    [InlineData("utf8mb3", "utf8mb3_general_ci", true)]
    [InlineData("utf8mb4", "binary", true)]
    [InlineData("utf8mb4", "ascii_general_ci", true)]
    public void The_database_default_and_a_core_table_default_decide_the_refusal(string databaseSet, string collation, bool refuses) =>
        (Refusal(databaseSet, Table(UsersTable, collation)) is not null).Should().Be(refuses);

    [Fact]
    public void A_database_without_core_tables_is_judged_on_its_default_alone()
    {
        Refusal("utf8mb4").Should().BeNull();
        Refusal("latin1").Should().NotBeNull();
    }

    [Fact]
    public void A_table_that_is_not_a_core_table_is_ignored() =>
        Refusal("utf8mb4", Table("blog_articles", Latin1Collation), Table("users", Latin1Collation), Table("other_struo_users", Latin1Collation))
            .Should().BeNull();

    [Fact]
    public void A_core_table_without_a_collation_is_skipped_and_a_missing_database_row_judges_the_tables_only()
    {
        Utf8Mb4Defaults.Refusal(Catalog(null, Table(UsersTable, null)), Prefix).Should().BeNull();
        Utf8Mb4Defaults.Refusal(Catalog(null, Table(UsersTable, Latin1Collation)), Prefix).Should().NotBeNull();
    }

    [Fact]
    public void Table_names_match_without_regard_to_case() =>
        Refusal("utf8mb4", Table("STRUO_USERS", Latin1Collation)).Should().Contain("`STRUO_USERS`");

    [Fact]
    public void The_version_table_is_a_core_table() =>
        Refusal("utf8mb4", Table("struo_schema_versions", Utf8Mb3Collation)).Should().Contain("`struo_schema_versions`");

    [Fact]
    public void Core_table_names_are_the_eleven_framework_tables_and_the_version_table_under_the_prefix()
    {
        var names = Utf8Mb4Defaults.CoreTableNames(Prefix);

        names.Should().HaveCount(FrameworkEntityTypes.All.Count + 1)
            .And.Contain([UsersTable, "struo_roles", "struo_user_sessions", "struo_file_translations", "struo_schema_versions"])
            .And.OnlyContain(n => n.StartsWith(Prefix));
        Utf8Mb4Defaults.CoreTableNames("").Should().Contain("users").And.Contain("schema_versions");
    }

    [Fact]
    public void A_non_default_table_prefix_selects_the_core_tables_under_it()
    {
        var catalog = Catalog(Schema("utf8mb4", UnicodeCollation),
            Table("cms_users", Latin1Collation), Table(UsersTable, Latin1Collation), Table("cms_schema_versions", Utf8Mb3Collation));

        var message = Utf8Mb4Defaults.Refusal(catalog, "cms_")!;

        message.Should().Contain("ALTER TABLE `cms_users` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")
            .And.Contain("ALTER TABLE `cms_schema_versions` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")
            .And.NotContain(UsersTable);
        Utf8Mb4Defaults.CoreTableNames("cms_").Should().OnlyContain(n => n.StartsWith("cms_"));
    }

    [Fact]
    public void When_the_database_offends_the_message_names_it_and_each_table_with_statements_for_exactly_those()
    {
        var message = Refusal(Schema("latin1", Latin1Collation),
            Table(UsersTable, Latin1Collation), Table("struo_roles", Utf8Mb4Collation), Table(FilesTable, Utf8Mb3Collation))!;

        message.Should().Contain("need utf8mb4")
            .And.Contain("Database `web_cms` has the default character set latin1.")
            .And.Contain("Table `struo_users` has the default collation latin1_swedish_ci.")
            .And.Contain("Table `struo_files` has the default collation utf8mb3_general_ci.")
            .And.Contain("ALTER DATABASE `web_cms` CHARACTER SET utf8mb4;")
            .And.Contain("ALTER TABLE `struo_users` CONVERT TO CHARACTER SET utf8mb4;")
            .And.Contain("ALTER TABLE `struo_files` CONVERT TO CHARACTER SET utf8mb4;")
            .And.Contain("run 'migrate' again")
            .And.NotContain("COLLATE")
            .And.NotContain("struo_roles");
    }

    [Fact]
    public void When_only_tables_offend_in_a_utf8mb4_database_each_statement_carries_the_collation_of_the_database()
    {
        var message = Refusal(Schema("utf8mb4", UnicodeCollation),
            Table(UsersTable, Utf8Mb3Collation), Table(FilesTable, Latin1Collation))!;

        message.Should().Contain("ALTER TABLE `struo_users` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")
            .And.Contain("ALTER TABLE `struo_files` CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")
            .And.NotContain("ALTER DATABASE")
            .And.NotContain("Database `");
    }

    [Fact]
    public void A_utf8mb4_database_whose_collation_is_not_visible_gets_statements_without_a_collation() =>
        Refusal(Schema("utf8mb4"), Table(UsersTable, Utf8Mb3Collation))!
            .Should().Contain("ALTER TABLE `struo_users` CONVERT TO CHARACTER SET utf8mb4;").And.NotContain("COLLATE");

    [Fact]
    public void The_message_ends_with_the_lines_about_tables_of_the_fork()
    {
        var lines = Refusal("latin1", Table(UsersTable, Latin1Collation))!.Split(Environment.NewLine);

        lines[^2].Should().Contain("Tables of your own").And.Contain("converted the same way");
        lines[^1].Should().Contain("'migrate:check' lists their columns");
    }

    [Fact]
    public void The_opening_line_is_true_whichever_part_offends()
    {
        var databaseOnly = Refusal("latin1")!;
        var tablesOnly = Refusal("utf8mb4", Table(UsersTable, Latin1Collation))!;

        foreach (var message in new[] { databaseOnly, tablesOnly })
            message.Split(Environment.NewLine)[0].Should().StartWith("Nothing was applied or changed")
                .And.NotContain("this database does not have it");
    }

    [Fact]
    public void Only_what_applies_is_listed()
    {
        var tablesOnly = Refusal("utf8mb4", Table(UsersTable, Latin1Collation))!;
        var databaseOnly = Refusal("latin1")!;

        tablesOnly.Should().NotContain("ALTER DATABASE").And.NotContain("Database `");
        databaseOnly.Should().Contain("ALTER DATABASE `web_cms` CHARACTER SET utf8mb4;").And.NotContain("ALTER TABLE").And.NotContain("Table `");
    }

    [Fact]
    public void Offending_tables_are_listed_in_name_order()
    {
        var message = Refusal("utf8mb4", Table(UsersTable, Latin1Collation), Table(FilesTable, Latin1Collation))!;

        message.IndexOf("`struo_files`", StringComparison.Ordinal).Should().BeLessThan(message.IndexOf("`struo_users`", StringComparison.Ordinal));
    }

    [Fact]
    public void A_backtick_in_a_name_is_doubled_in_the_statements()
    {
        var catalog = new CatalogDefaults("we`b", [Schema("latin1")], []);

        Utf8Mb4Defaults.Refusal(catalog, Prefix).Should().Contain("ALTER DATABASE `we``b` CHARACTER SET utf8mb4;");
        Utf8Mb4Defaults.Quote("a`b").Should().Be("`a``b`");
    }

    [Theory]
    [InlineData("utf8mb4_0900_ai_ci", "utf8mb4")]
    [InlineData("utf8mb4_uca1400_nopad_ai_ci", "utf8mb4")]
    [InlineData("utf8mb3_uca1400_ai_ci", "utf8mb3")]
    [InlineData("utf8mb3_general_ci", "utf8mb3")]
    [InlineData("latin1_swedish_ci", "latin1")]
    [InlineData("ascii_general_ci", "ascii")]
    [InlineData("utf16_uca1400_ai_ci", "utf16")]
    [InlineData("binary", "binary")]
    public void The_character_set_is_the_collation_name_up_to_its_first_underscore(string collation, string expected) =>
        Utf8Mb4Defaults.CharacterSetOf(collation).Should().Be(expected);

    [Theory]
    [InlineData(StruoDbType.MySql, true)]
    [InlineData(StruoDbType.Sqlite, false)]
    [InlineData(StruoDbType.PostgreSQL, false)]
    [InlineData(StruoDbType.SqlServer, false)]
    [InlineData(StruoDbType.Oracle, false)]
    public void Only_MySql_reads_the_catalog(StruoDbType dbType, bool reads)
    {
        var calls = 0;
        CatalogDefaults Read()
        {
            calls++;
            return Catalog(Schema("latin1"), Table(UsersTable, Latin1Collation));
        }

        var catalog = Utf8Mb4Defaults.CatalogFor(dbType, Read);

        calls.Should().Be(reads ? 1 : 0);
        (catalog is not null).Should().Be(reads);
    }

    [Fact]
    public void Every_StruoDbType_value_is_covered_by_the_gate_theory() =>
        Enum.GetValues<StruoDbType>().Should().BeEquivalentTo(
            [StruoDbType.MySql, StruoDbType.Sqlite, StruoDbType.PostgreSQL, StruoDbType.SqlServer, StruoDbType.Oracle]);

    [Fact]
    public void MySql_with_utf8mb4_defaults_passes_after_reading() =>
        Refusal(Schema("utf8mb4", Utf8Mb4Collation), Table(UsersTable, Utf8Mb4Collation)).Should().BeNull();

    [Theory]
    [InlineData(typeof(SchemataRow))]
    [InlineData(typeof(TablesRow))]
    public void The_catalog_read_models_are_not_tables_or_collections(Type model)
    {
        model.GetCustomAttributes(typeof(SugarTable), inherit: true).Should().BeEmpty();
        model.GetCustomAttributes(typeof(CmsCollectionAttribute), inherit: true).Should().BeEmpty();
        FrameworkEntityTypes.All.Should().NotContain(model);
    }
}
