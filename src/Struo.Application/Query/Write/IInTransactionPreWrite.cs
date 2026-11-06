// src/Struo.Application/Query/Write/IInTransactionPreWrite.cs
namespace Struo.Application.Query;

/// <summary>
/// A core-only step that runs first inside the write transaction, before the transaction's first read or write.
/// A rule that must act on a state no concurrent transaction can change takes its locks and reads here: a
/// read made earlier, outside the transaction, can be stale by the time the write runs. An interceptor that
/// implements it (explicitly, so it is no part of that interceptor's public surface) is called after the
/// interceptors that came before it in registration order are, and a throw rolls the write back.
/// <para>
/// It runs for Create, Update and Revert on every write, and for Trash, Restore and Purge when their own
/// transaction opens: Trash runs it even when the row is already in the trash; Purge when the row exists; Restore
/// only on a soft-deletable collection, the only one whose restore opens a transaction. It does not run when an
/// <see cref="ICollectionDeleteHandler"/> replaces the delete.
/// </para>
/// </summary>
internal interface IInTransactionPreWrite
{
    Task BeforeWriteInTransactionAsync(ItemWriteContext context, CancellationToken ct);
}
