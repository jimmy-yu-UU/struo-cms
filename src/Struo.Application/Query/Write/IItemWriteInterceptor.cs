// src/Struo.Application/Query/Write/IItemWriteInterceptor.cs
namespace Struo.Application.Query;

/// <summary>
/// Hooks the item write path of the collections it applies to, without editing <see cref="ItemService"/>.
/// Register with <c>services.AddScoped&lt;IItemWriteInterceptor, MyInterceptor&gt;()</c> (any number);
/// interceptors run in registration order, core ones first. Every hook defaults to a no-op, so an
/// implementation overrides only what it needs. A hook may throw a <c>StruoDomainException</c> or a
/// <c>QueryException</c> to refuse the write with a 4xx.
/// <para>
/// Hooks run for the operations of <see cref="ItemWriteOperation"/> on <see cref="ItemService"/> only:
/// <c>POST /api/files</c>, <c>POST /api/users</c> and the other dedicated endpoints do not run them, and
/// the children of a cascaded purge do not either (<see cref="Changes.IItemChangeListener"/> still sees
/// them).
/// </para>
/// </summary>
public interface IItemWriteInterceptor
{
    /// <summary>True when this interceptor handles writes to <paramref name="collection"/>, the canonical
    /// collection name (<c>CollectionMetadata.Name</c>).</summary>
    bool AppliesTo(string collection);

    /// <summary>
    /// Runs outside the transaction, after the request body is merged into <see cref="ItemWriteContext.Entity"/>
    /// and before the Required check, so it may fill a Required field or change any other. For trash,
    /// restore and purge it runs with the existing row and no body. Not called for an unknown id.
    /// </summary>
    Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Runs inside the write transaction, after the row, many-to-many links and translations are written
    /// and before the revision is captured. A throw rolls the whole write back. For trash, restore and
    /// purge it runs only when the row was actually trashed, restored or purged.
    /// </summary>
    Task InTransactionAsync(ItemWriteContext context, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Runs once the write is committed, before <see cref="Changes.IItemChangeNotifier"/> notifies its
    /// listeners (cache invalidation, session revocation). A throw skips the remaining interceptors'
    /// hooks, the notification still goes out, then the exception propagates although the write stays
    /// committed. It runs only when the write happened, except for trash and restore, which run it
    /// whenever the row exists: check <see cref="ItemWriteContext.Changed"/> to act on real changes
    /// only. <paramref name="ct"/> is not tied to the request.
    /// </summary>
    Task AfterCommitAsync(ItemWriteContext context, CancellationToken ct) => Task.CompletedTask;
}
