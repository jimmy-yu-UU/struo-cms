// src/Struo.Infrastructure/Query/TransactionRunner.cs
using SqlSugar;

namespace Struo.Infrastructure.Query;

internal sealed class TransactionRunner(ISqlSugarClient db)
{
    public async Task InTransactionAsync(Func<Task> body, CancellationToken ct = default) =>
        await InTransactionAsync(async () =>
        {
            await body();
            return true;
        }, ct);

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> body, CancellationToken ct = default)
    {
        // Nesting-safe: if the scoped connection already has an open transaction (an outer
        // InTransactionAsync), join it rather than opening — and committing — a second one, which
        // would end the outer transaction early.
        if (db.Ado.Transaction is not null)
            return await body();

        try
        {
            // BeginTranAsync/CommitTranAsync/RollbackTranAsync have no CancellationToken overloads
            // (SqlSugar 5.1.4.216); the token is honored by the awaited ORM calls inside body().
            await db.Ado.BeginTranAsync();
            var result = await body();
            await db.Ado.CommitTranAsync();
            return result;
        }
        catch
        {
            await db.Ado.RollbackTranAsync();
            throw;
        }
    }
}
