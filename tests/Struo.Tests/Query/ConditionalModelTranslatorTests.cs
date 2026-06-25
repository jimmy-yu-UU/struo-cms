// tests/Struo.Tests/Query/ConditionalModelTranslatorTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Struo.Domain.Query;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ConditionalModelTranslatorTests
{
    private static (ISqlSugarClient db, EntityDescriptor d, SqliteTestDatabase file) Setup()
    {
        var file = new SqliteTestDatabase();
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = file.ConnectionString },
            new TestCurrentUserAccessor("system"));
        db.CodeFirst.InitTables<Article>();
        var fieldToProp = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["title"] = "Title", ["status"] = "Status" };
        var d = new EntityDescriptor(typeof(Article), fieldToProp, "Id");
        return (db, d, file);
    }

    [Fact]
    public void Translates_single_equality()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("status", QueryOperator.Eq, "published"), null, [], d, db);

            list.Should().ContainSingle();
            var cm = (ConditionalModel)list[0];
            cm.FieldName.Should().Be("Status");
            cm.ConditionalType.Should().Be(ConditionalType.Equal);
            cm.FieldValue.Should().Be("published");
        }
    }

    [Fact]
    public void Translates_contains_to_like()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(
                new ComparisonFilter("title", QueryOperator.Contains, "x"), null, [], d, db);
            ((ConditionalModel)list[0]).ConditionalType.Should().Be(ConditionalType.Like);
        }
    }

    [Fact]
    public void Search_builds_or_group_over_searchable_fields()
    {
        var (db, d, file) = Setup();
        using (file)
        {
            var list = ConditionalModelTranslator.Translate(null, "hello", ["title"], d, db);
            list.Should().ContainSingle();
            list[0].Should().BeOfType<ConditionalCollections>();
        }
    }
}
