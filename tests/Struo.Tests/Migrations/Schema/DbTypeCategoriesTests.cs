using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations.Schema;
using Xunit;

namespace Struo.Tests.Migrations.Schema;

public sealed class DbTypeCategoriesTests
{
    private const StruoDbType Pg = StruoDbType.PostgreSQL;
    private const StruoDbType Ms = StruoDbType.SqlServer;
    private const StruoDbType My = StruoDbType.MySql;

    private static DbColumnInfo Col(string type, int length = 0) => new() { DataType = type, Length = length };

    [Theory]
    [InlineData(Pg, ColumnCategory.Decimal, "numeric", 0)]
    [InlineData(Pg, ColumnCategory.Double, "float8", 0)]
    [InlineData(Pg, ColumnCategory.Double, "float4", 0)]
    [InlineData(Pg, ColumnCategory.Binary, "bytea", 0)]
    [InlineData(Pg, ColumnCategory.Boolean, "bool", 0)]
    [InlineData(Pg, ColumnCategory.LongText, "json", 0)]
    [InlineData(Pg, ColumnCategory.LongText, "jsonb", 0)]
    [InlineData(Pg, ColumnCategory.DateTime, "date", 0)]
    [InlineData(Pg, ColumnCategory.String, "text", 0)]
    [InlineData(Ms, ColumnCategory.Decimal, "decimal", 18)]
    [InlineData(Ms, ColumnCategory.Decimal, "money", 19)]
    [InlineData(Ms, ColumnCategory.Double, "float", 53)]
    [InlineData(Ms, ColumnCategory.Binary, "varbinary", -1)]
    [InlineData(Ms, ColumnCategory.Boolean, "bit", 1)]
    [InlineData(Ms, ColumnCategory.LongText, "nvarchar", -1)]
    [InlineData(Ms, ColumnCategory.String, "nvarchar", -1)]
    [InlineData(Ms, ColumnCategory.String, "ntext", 16)]
    [InlineData(Ms, ColumnCategory.DateTime, "date", 3)]
    [InlineData(My, ColumnCategory.Decimal, "decimal", 0)]
    [InlineData(My, ColumnCategory.Double, "double", 0)]
    [InlineData(My, ColumnCategory.Binary, "longblob", 0)]
    [InlineData(My, ColumnCategory.Boolean, "tinyint", 1)]
    [InlineData(My, ColumnCategory.Integer, "tinyint", 3)]
    [InlineData(My, ColumnCategory.Guid, "varchar", 36)]
    [InlineData(My, ColumnCategory.LongText, "json", 0)]
    [InlineData(My, ColumnCategory.String, "longtext", 0)]
    [InlineData(My, ColumnCategory.String, "mediumtext", 0)]
    [InlineData(My, ColumnCategory.DateTime, "date", 0)]
    public void Matches_accepts(StruoDbType db, ColumnCategory expected, string type, int length) =>
        DbTypeCategories.Matches(db, expected, Col(type, length)).Should().BeTrue();

    [Theory]
    [InlineData(My, ColumnCategory.Boolean, "tinyint", 3)]
    [InlineData(My, ColumnCategory.Integer, "tinyint", 1)]
    [InlineData(My, ColumnCategory.Guid, "varchar", 255)]
    [InlineData(Pg, ColumnCategory.LongText, "varchar", 255)]
    [InlineData(Pg, ColumnCategory.Decimal, "float8", 0)]
    [InlineData(Ms, ColumnCategory.String, "int", 10)]
    [InlineData(Ms, ColumnCategory.Boolean, "int", 10)]
    public void Matches_rejects(StruoDbType db, ColumnCategory expected, string type, int length) =>
        DbTypeCategories.Matches(db, expected, Col(type, length)).Should().BeFalse();

    [Theory]
    [InlineData(Ms, 255, "varchar", -1, true)]
    [InlineData(Ms, 255, "nvarchar", 255, true)]
    [InlineData(Ms, 255, "nvarchar", 100, false)]
    [InlineData(My, 255, "varchar", 100, false)]
    [InlineData(My, 255, "longtext", 0, true)]
    [InlineData(Pg, 255, "text", 0, true)]
    [InlineData(Pg, 255, "varchar", 0, true)]
    [InlineData(Pg, 255, "varchar", 100, false)]
    public void LengthMatches_follows_the_rules(StruoDbType db, int expected, string type, int length, bool result) =>
        DbTypeCategories.LengthMatches(db, expected, Col(type, length)).Should().Be(result);

    [Fact]
    public void Oracle_types_are_not_supported()
    {
        DbTypeCategories.TypesSupported(StruoDbType.Oracle).Should().BeFalse();
        DbTypeCategories.TypesSupported(Pg).Should().BeTrue();
    }
}
