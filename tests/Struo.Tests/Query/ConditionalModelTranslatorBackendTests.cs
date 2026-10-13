using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Domain.Query;
using Struo.Infrastructure.Files;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// <see cref="ConditionalModelTranslator"/> for each backend without a database. Building a conditional
/// model reads only entity metadata, so each client points at a closed port: any connection attempt
/// would fail the test.
/// </summary>
public class ConditionalModelTranslatorBackendTests
{
    private static readonly EntityDescriptor Folder = new(typeof(MediaFolder),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = "CreatedAt", ["name"] = "Name", ["missing"] = "NoSuchProperty"
        }, "Id");

    private static ISqlSugarClient Client(string backend) => SqlSugarClientFactory.Create(
        new DatabaseOptions
        {
            DbType = Enum.Parse<StruoDbType>(backend),
            ConnectionString = backend switch
            {
                "PostgreSQL" => "Host=127.0.0.1;Port=1;Database=none_test;Username=none;Password=none;Timeout=1",
                "SqlServer" => "Server=127.0.0.1,1;Database=none_test;User Id=none;Password=none;Connect Timeout=1;TrustServerCertificate=true",
                "MySql" => "Server=127.0.0.1;Port=1;Database=none_test;Uid=none;Pwd=none;Connection Timeout=1",
                _ => "Data Source=none_test.db;Mode=ReadOnly",
            },
        },
        new TestCurrentUserAccessor(Guid.Empty));

    private static bool ZoneAware(string backend) => backend is "PostgreSQL" or "SqlServer";

    private static string Expected(string backend) =>
        ZoneAware(backend) ? "2026-03-04T05:06:07.0000000+00:00" : "2026-03-04 05:06:07";

    public static TheoryData<string, string> BackendsAndForms()
    {
        var data = new TheoryData<string, string>();
        foreach (var backend in new[] { "PostgreSQL", "SqlServer", "MySql", "Sqlite" })
            foreach (var form in UtcInstantInput.Forms)
                data.Add(backend, form);
        return data;
    }

    [Theory]
    [MemberData(nameof(BackendsAndForms))]
    public void Every_instant_operator_binds_the_utc_instant(string backend, string form)
    {
        var db = Client(backend);
        var value = UtcInstantInput.Of(form);

        foreach (var (op, type) in new[]
                 {
                     (QueryOperator.Eq, ConditionalType.Equal), (QueryOperator.Neq, ConditionalType.NoEqual),
                     (QueryOperator.Gt, ConditionalType.GreaterThan), (QueryOperator.Gte, ConditionalType.GreaterThanOrEqual),
                     (QueryOperator.Lt, ConditionalType.LessThan), (QueryOperator.Lte, ConditionalType.LessThanOrEqual),
                 })
        {
            var cm = ConditionalModelTranslator.ToSingleModel(new ComparisonFilter("createdAt", op, value), Folder, db);
            cm.ConditionalType.Should().Be(type);
            cm.FieldName.Should().Be(db.EntityMaintenance.GetDbColumnName<MediaFolder>(nameof(MediaFolder.CreatedAt)));
            cm.FieldValue.Should().Be(Expected(backend));
            cm.CSharpTypeName.Should().Be(ZoneAware(backend) ? "datetimeoffset" : "datetime");
        }
    }

    [Theory]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    [InlineData("MySql")]
    [InlineData("Sqlite")]
    public void Query_string_in_and_nin_split_on_commas(string backend)
    {
        var db = Client(backend);
        const string list = "2026-03-04T05:06:07Z,2026-03-04T13:06:07+08:00";

        var inModel = ConditionalModelTranslator.ToSingleModel(new ComparisonFilter("createdAt", QueryOperator.In, list), Folder, db);
        var ninModel = ConditionalModelTranslator.ToSingleModel(new ComparisonFilter("createdAt", QueryOperator.Nin, list), Folder, db);

        inModel.ConditionalType.Should().Be(ConditionalType.In);
        ninModel.ConditionalType.Should().Be(ConditionalType.NotIn);
        inModel.FieldValue.Should().Be(Expected(backend) + "," + Expected(backend));
        ninModel.FieldValue.Should().Be(inModel.FieldValue);
    }

    [Theory]
    [InlineData("PostgreSQL")]
    [InlineData("SqlServer")]
    public void Text_operators_null_checks_and_unparseable_values_keep_the_general_path(string backend)
    {
        var db = Client(backend);

        var startsWith = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("createdAt", QueryOperator.StartsWith, "2026-03-04"), Folder, db);
        startsWith.ConditionalType.Should().Be(ConditionalType.LikeLeft);
        startsWith.FieldValue.Should().Be("2026-03-04");
        startsWith.CSharpTypeName.Should().Be("datetime");

        var garbage = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("createdAt", QueryOperator.Gt, "not-a-date"), Folder, db);
        garbage.FieldValue.Should().Be("not-a-date");
        garbage.CSharpTypeName.Should().Be("datetime");

        var nullValue = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("createdAt", QueryOperator.Eq, null), Folder, db);
        nullValue.FieldValue.Should().BeNull();

        var isNull = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("createdAt", QueryOperator.Null, null), Folder, db);
        isNull.ConditionalType.Should().Be(ConditionalType.EqualNull);
        isNull.FieldValue.Should().BeNull();
        isNull.CSharpTypeName.Should().BeNull();
    }

    [Fact]
    public void A_logical_filter_translates_each_comparison_child()
    {
        var db = Client("PostgreSQL");
        var filter = new LogicalFilter(LogicalOperator.Or,
        [
            new ComparisonFilter("createdAt", QueryOperator.Lt, "2026-03-04T05:06:07Z"),
            new ComparisonFilter("name", QueryOperator.Eq, "A"),
        ]);

        var models = ConditionalModelTranslator.Translate(filter, null, [], Folder, db);

        var children = ((ConditionalCollections)models.Single()).ConditionalList;
        children.Should().HaveCount(2);
        children.Should().OnlyContain(kv => kv.Key == WhereType.Or);
        children[0].Value.FieldValue.Should().Be("2026-03-04T05:06:07.0000000+00:00");
        children[1].Value.FieldValue.Should().Be("A");
    }

    [Fact]
    public void Unknown_field_unknown_node_and_unmapped_operator_are_refused()
    {
        var db = Client("PostgreSQL");

        FluentActions.Invoking(() => ConditionalModelTranslator.ToSingleModel(
                new ComparisonFilter("nope", QueryOperator.Eq, "x"), Folder, db))
            .Should().Throw<ArgumentException>().WithMessage("*Unknown field path 'nope'*");
        FluentActions.Invoking(() => ConditionalModelTranslator.Translate(
                new RelationPredicateFilter("parent", RelationQuantifier.Some,
                    new ComparisonFilter("name", QueryOperator.Eq, "A")), null, [], Folder, db))
            .Should().Throw<InvalidOperationException>().WithMessage("*Unknown filter node type*");
        FluentActions.Invoking(() => ConditionalModelTranslator.ToSingleModel(
                new ComparisonFilter("name", (QueryOperator)999, "x"), Folder, db))
            .Should().Throw<InvalidOperationException>().WithMessage("*Unmapped operator*");
    }
}
