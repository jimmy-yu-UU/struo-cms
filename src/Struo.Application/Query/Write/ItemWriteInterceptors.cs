// src/Struo.Application/Query/Write/ItemWriteInterceptors.cs
using System.Text.Json;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>The registered interceptors and delete handlers, resolved once per <see cref="ItemService"/>.</summary>
internal sealed class ItemWriteInterceptors(
    IEnumerable<IItemWriteInterceptor>? interceptors, IEnumerable<ICollectionDeleteHandler>? deleteHandlers)
{
    private readonly IItemWriteInterceptor[] all = interceptors?.ToArray() ?? [];
    private readonly ICollectionDeleteHandler[] handlers = deleteHandlers?.ToArray() ?? [];

    /// <summary>Starts one write: the hooks of the interceptors that apply to <paramref name="meta"/>.</summary>
    public ItemWriteHooks Begin(
        ItemWriteOperation operation, CollectionMetadata meta, object entity, string? id,
        IReadOnlySet<string>? bodyKeys = null, JsonElement? body = null)
    {
        var applicable = all.Where(i => i.AppliesTo(meta.Name)).ToArray();
        return new ItemWriteHooks(
            new ItemWriteContext(operation, meta, entity, id, bodyKeys ?? new HashSet<string>(), body), applicable);
    }

    /// <summary>True when an interceptor applies, so a caller can skip the reads only hooks need.</summary>
    public bool AnyApplies(CollectionMetadata meta) => all.Any(i => i.AppliesTo(meta.Name));

    public ICollectionDeleteHandler? DeleteHandlerFor(CollectionMetadata meta) =>
        handlers.FirstOrDefault(h => h.AppliesTo(meta.Name));
}

/// <summary>The hooks of one write, run over the interceptors that apply in registration order.</summary>
internal sealed class ItemWriteHooks(ItemWriteContext context, IReadOnlyList<IItemWriteInterceptor> interceptors)
{
    /// <summary>The hooks of a write no interceptor sees: every method is a no-op.</summary>
    public static readonly ItemWriteHooks Empty = new(null!, []);

    public ItemWriteContext Context { get; } = context;

    /// <summary>Records the stored entity and id once the row is written.</summary>
    public void Stored(object entity, object id)
    {
        Context.Entity = entity;
        Context.Id = id.ToString();
    }

    public Task BeforeWriteAsync(CancellationToken ct) => RunAsync(i => i.BeforeWriteAsync(Context, ct));

    public Task InTransactionAsync(CancellationToken ct) => RunAsync(i => i.InTransactionAsync(Context, ct));

    // The write is committed by now: a disconnecting caller must not skip these.
    public Task AfterCommitAsync() => RunAsync(i => i.AfterCommitAsync(Context, CancellationToken.None));

    private async Task RunAsync(Func<IItemWriteInterceptor, Task> hook)
    {
        foreach (var interceptor in interceptors)
            await hook(interceptor);
    }
}
