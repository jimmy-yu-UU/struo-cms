using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Domain.Query;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;
using Xunit;
using File = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Query;

/// <summary>
/// <see cref="ConditionalModelTranslator"/> on plain DateTime columns (no time zone), per backend and
/// without a database. Every backend compares the UTC wall clock as a datetime. SQL Server gets an ISO 8601
/// text cut to milliseconds, which its legacy datetime column reads and rounds the way it rounded the
/// stored value.
/// </summary>
public class ConditionalModelTranslatorPlainDateTimeTests
{
    private static readonly string[] Backends = ["PostgreSQL", "SqlServer", "MySql", "Sqlite"];

    private static readonly EntityDescriptor Plain = new(typeof(File),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["createdAt"] = "CreatedAt", ["deletedAt"] = "DeletedAt", ["size"] = "Size"
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

    private static string Expected(string backend) =>
        backend == "SqlServer" ? "2026-03-04T05:06:07.000" : "2026-03-04 05:06:07";


    public static TheoryData<string, string> BackendsAndForms()
    {
        var data = new TheoryData<string, string>();
        foreach (var backend in Backends)
            foreach (var form in UtcInstantInput.Forms)
                data.Add(backend, form);
        return data;
    }

    public static TheoryData<string> BackendNames() => new(Backends);

    [Theory]
    [MemberData(nameof(BackendsAndForms))]
    public void Every_instant_operator_compares_the_utc_instant(string backend, string form)
    {
        var db = Client(backend);
        var value = UtcInstantInput.Of(form);

        foreach (var field in new[] { "createdAt", "deletedAt" })
            foreach (var op in new[]
                     {
                         QueryOperator.Eq, QueryOperator.Neq, QueryOperator.Gt, QueryOperator.Gte,
                         QueryOperator.Lt, QueryOperator.Lte,
                     })
            {
                var cm = ConditionalModelTranslator.ToSingleModel(new ComparisonFilter(field, op, value), Plain, db);
                cm.FieldValue.Should().Be(Expected(backend));
                cm.CSharpTypeName.Should().Be("datetime");
            }
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void Mixed_in_and_nin_values_are_converted_one_by_one(string backend)
    {
        var db = Client(backend);
        const string list = "2026-03-04T05:06:07Z,2026-03-04T10:36:07+05:30,2026-03-04T05:06:07";

        var inModel = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("createdAt", QueryOperator.In, list), Plain, db);
        var ninModel = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("createdAt", QueryOperator.Nin,
                new List<object> { "2026-03-04T10:36:07+05:30", "2026-03-04T05:06:07Z" }), Plain, db);

        inModel.ConditionalType.Should().Be(ConditionalType.In);
        ninModel.ConditionalType.Should().Be(ConditionalType.NotIn);
        inModel.FieldValue.Should().Be(string.Join(",", Enumerable.Repeat(Expected(backend), 3)));
        ninModel.FieldValue.Should().Be(string.Join(",", Enumerable.Repeat(Expected(backend), 2)));
        inModel.CSharpTypeName.Should().Be("datetime");
    }

    [Theory]
    [MemberData(nameof(BackendNames))]
    public void Other_input_keeps_the_general_path(string backend)
    {
        var db = Client(backend);

        var garbage = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("createdAt", QueryOperator.Gt, "not-a-date"), Plain, db);
        garbage.FieldValue.Should().Be("not-a-date");
        garbage.CSharpTypeName.Should().Be("datetime");

        var nullValue = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("deletedAt", QueryOperator.Eq, null), Plain, db);
        nullValue.FieldValue.Should().BeNull();
        nullValue.CSharpTypeName.Should().Be("datetime");

        var startsWith = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("createdAt", QueryOperator.StartsWith, "2026-03-04"), Plain, db);
        startsWith.FieldValue.Should().Be("2026-03-04");

        var notDateTime = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("size", QueryOperator.Eq, "5"), Plain, db);
        notDateTime.FieldValue.Should().Be("5");
        notDateTime.CSharpTypeName.Should().Be("long");
    }

    [Fact]
    public void SQL_Server_text_is_rounded_to_milliseconds()
    {
        var db = Client("SqlServer");

        var cm = ConditionalModelTranslator.ToSingleModel(new ComparisonFilter("createdAt", QueryOperator.In,
            "2026-03-04T05:06:07.1234567Z,2026-03-04T05:06:07.9996Z"), Plain, db);

        cm.FieldValue.Should().Be("2026-03-04T05:06:07.123,2026-03-04T05:06:08.000");
    }

    private sealed class StampTranslation
    {
        public long Id { get; set; }
        public Guid ParentId { get; set; }
        public DateTime? ValidFrom { get; set; }
    }

    // Relation pushdown builds a camelCase descriptor for a translation sidecar the same way.
    [Theory]
    [MemberData(nameof(BackendNames))]
    public void A_synthetic_translation_descriptor_takes_the_instant_path(string backend)
    {
        var props = typeof(StampTranslation).GetProperties();
        var descriptor = new EntityDescriptor(typeof(StampTranslation),
            props.ToDictionary(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => p.Name,
                StringComparer.OrdinalIgnoreCase), "Id");

        var cm = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("validFrom", QueryOperator.Gte, "2026-03-04T10:36:07+05:30"), descriptor, Client(backend));

        cm.FieldValue.Should().Be(Expected(backend));
        cm.CSharpTypeName.Should().Be("datetime");
    }

    private sealed class DatedRow
    {
        public Guid Id { get; set; }
        public DateOnly Day { get; set; }
    }

    [Fact]
    public void A_dateonly_property_does_not_enter_the_instant_path()
    {
        var db = Client("SqlServer");
        var dated = new EntityDescriptor(typeof(DatedRow),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["day"] = "Day" }, "Id");

        var day = ConditionalModelTranslator.ToSingleModel(
            new ComparisonFilter("day", QueryOperator.Eq, "2026-03-04T10:00:00+05:30"), dated, db);
        day.FieldValue.Should().Be("2026-03-04T10:00:00+05:30");
        day.CSharpTypeName.Should().NotBe("datetimeoffset");
    }
}
