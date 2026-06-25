using FluentAssertions;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;
using Xunit;

namespace Struo.Tests.Persistence;

public class DbTypeMapperTests
{
    [Theory]
    [InlineData(StruoDbType.PostgreSQL, SqlSugar.DbType.PostgreSQL)]
    [InlineData(StruoDbType.MySql, SqlSugar.DbType.MySql)]
    [InlineData(StruoDbType.SqlServer, SqlSugar.DbType.SqlServer)]
    [InlineData(StruoDbType.Sqlite, SqlSugar.DbType.Sqlite)]
    [InlineData(StruoDbType.Oracle, SqlSugar.DbType.Oracle)]
    public void Map_returns_matching_SqlSugar_DbType(StruoDbType input, SqlSugar.DbType expected)
    {
        DbTypeMapper.Map(input).Should().Be(expected);
    }
}
