using SqlSugar;
using Struo.Application.Abstractions;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Persistence;

public static class SqlSugarClientFactory
{
    public static ISqlSugarClient Create(DatabaseOptions options, ICurrentUserAccessor currentUser)
    {
        var client = new SqlSugarClient(new ConnectionConfig
        {
            ConnectionString = options.ConnectionString,
            DbType = DbTypeMapper.Map(options.DbType),
            IsAutoCloseConnection = true
        });

        AuditAop.Register(client, currentUser);
        return client;
    }
}
