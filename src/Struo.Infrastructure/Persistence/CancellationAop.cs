using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Makes a cancelled token surface as <see cref="OperationCanceledException"/> on SQL Server, where
/// SqlSugar wraps the cancelled connection open in a <see cref="SqlSugarException"/>. See
/// <c>docs/ai/decisions/sqlserver-cancellation-surfaces-as-sqlsugar-exception.md</c>.
/// </summary>
internal static class CancellationAop
{
    public static void Register(ISqlSugarClient client)
    {
        if (client.CurrentConnectionConfig.DbType != DbType.SqlServer) return;

        // OnError runs inside the catch of every Ado command; throwing here replaces the rethrow.
        client.Aop.OnError = error =>
        {
            if (Translate(error, client.Ado.CancellationToken) is { } cancelled) throw cancelled;
        };
    }

    // The cancellation to throw in place of `error`, or null when the error is an ordinary failure.
    internal static OperationCanceledException? Translate(Exception error, CancellationToken? token)
    {
        if (token is { IsCancellationRequested: true } cancelled)
            return new OperationCanceledException(error.Message, error, cancelled);
        if (error.InnerException is OperationCanceledException inner)
            return new OperationCanceledException(error.Message, error, inner.CancellationToken);
        return null;
    }
}
