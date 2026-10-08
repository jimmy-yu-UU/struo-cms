using AwesomeAssertions;
using FluentMigrator.Builders.Create.Table;
using FluentMigrator.Expressions;
using FluentMigrator.Model;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Migrations;

public sealed class ColumnTypeExtensionsTests
{
    private static ColumnDefinition Column(Action<ICreateTableColumnAsTypeSyntax> type)
    {
        var expression = new CreateTableExpression { TableName = "probe" };
        type(new CreateTableExpressionBuilder(expression, null!).WithColumn("at"));
        return expression.Columns.Single();
    }

    [Theory]
    [InlineData(StruoDbType.PostgreSQL)]
    [InlineData(StruoDbType.SqlServer)]
    [InlineData(StruoDbType.Sqlite)]
    [InlineData(StruoDbType.Oracle)]
    public void AsTimestamp_is_AsDateTime_outside_MySql(StruoDbType db)
    {
        var timestamp = Column(c => c.AsTimestamp(db));
        var dateTime = Column(c => c.AsDateTime());

        timestamp.Type.Should().Be(System.Data.DbType.DateTime).And.Be(dateTime.Type);
        timestamp.CustomType.Should().BeNull();
        timestamp.Size.Should().Be(dateTime.Size);
        timestamp.Precision.Should().Be(dateTime.Precision);
    }

    [Fact]
    public void AsTimestamp_is_datetime_6_on_MySql() =>
        Column(c => c.AsTimestamp(StruoDbType.MySql)).CustomType.Should().Be("datetime(6)");

    [Theory]
    [InlineData(StruoDbType.PostgreSQL)]
    [InlineData(StruoDbType.SqlServer)]
    [InlineData(StruoDbType.Sqlite)]
    [InlineData(StruoDbType.Oracle)]
    public void AsShape_Timestamp_is_AsDateTime_outside_MySql(StruoDbType db)
    {
        var shaped = Column(c => c.AsShape(ColumnShape.Timestamp, db));
        var dateTime = Column(c => c.AsDateTime());

        shaped.Type.Should().Be(System.Data.DbType.DateTime).And.Be(dateTime.Type);
        shaped.CustomType.Should().BeNull();
        shaped.Size.Should().Be(dateTime.Size);
        shaped.Precision.Should().Be(dateTime.Precision);
    }

    [Fact]
    public void AsShape_Timestamp_is_datetime_6_on_MySql() =>
        Column(c => c.AsShape(ColumnShape.Timestamp, StruoDbType.MySql)).CustomType.Should().Be("datetime(6)");
}
