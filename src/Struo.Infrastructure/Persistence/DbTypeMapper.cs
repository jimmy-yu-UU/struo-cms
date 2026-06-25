using Struo.Application.Configuration;

namespace Struo.Infrastructure.Persistence;

public static class DbTypeMapper
{
    public static SqlSugar.DbType Map(StruoDbType dbType) => dbType switch
    {
        StruoDbType.PostgreSQL => SqlSugar.DbType.PostgreSQL,
        StruoDbType.MySql      => SqlSugar.DbType.MySql,
        StruoDbType.SqlServer  => SqlSugar.DbType.SqlServer,
        StruoDbType.Sqlite     => SqlSugar.DbType.Sqlite,
        StruoDbType.Oracle     => SqlSugar.DbType.Oracle,
        _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unsupported DbType")
    };
}
