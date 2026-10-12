using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Makes a cancelled token surface as <see cref="OperationCanceledException"/> on SQL Server, where
/// SqlSugar wraps the cancelled connection open in a <see cref="SqlSugarException"/>. See
/// <c>docs/ai/decisions/sqlserver-cancellation-surfaces-as-sqlsugar-exception.md</c>.
/// </summary>
internal static class CancellationAop
{
    public static void Register(SqlSugarClient client)
    {
        if (client.CurrentConnectionConfig.DbType != DbType.SqlServer) return;

        // OnError runs inside the catch of every Ado command; throwing here replaces the rethrow.
        client.Aop.OnError = error =>
        {
            if (client.Ado.CancellationToken is { IsCancellationRequested: true } token)
                throw new OperationCanceledException(error.Message, error, token);
        };
    }
}
