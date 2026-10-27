// src/Struo.Application/Query/Write/ItemWriteContext.cs
using System.Text.Json;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>The write an <see cref="IItemWriteInterceptor"/> is called for.</summary>
public enum ItemWriteOperation
{
    Create,
    Update,

    /// <summary>An update that re-applies a past revision's snapshot.</summary>
    Revert,

    /// <summary>Soft delete.</summary>
    Trash,
    Restore,

    /// <summary>Permanent removal.</summary>
    Purge,
}

/// <summary>What an <see cref="IItemWriteInterceptor"/> hook sees of one write.</summary>
public sealed record ItemWriteContext
{
    public ItemWriteContext(
        ItemWriteOperation operation, CollectionMetadata meta, object entity, string? id,
        IReadOnlySet<string> bodyKeys, JsonElement? body, object? existing = null)
    {
        Operation = operation;
        Meta = meta;
        Entity = entity;
        Id = id;
        BodyKeys = bodyKeys;
        Body = body;
        Existing = existing;
    }

    public ItemWriteOperation Operation { get; }

    public CollectionMetadata Meta { get; }

    /// <summary>
    /// The entity being written, mutable until the transaction runs: for create, update and revert the
    /// request body merged into it; for trash, restore and purge the existing row. After the row is
    /// written it is the stored entity.
    /// </summary>
    public object Entity { get; init; }

    /// <summary>The item id; null for a create until the row is inserted.</summary>
    public string? Id { get; init; }

    /// <summary>
    /// For update and revert, the row as stored before the body was merged (a separate read, loaded only
    /// when an interceptor applies), so a hook can compare old and new. Null for create, trash, restore
    /// and purge, where <see cref="Entity"/> is the row.
    /// </summary>
    public object? Existing { get; }

    /// <summary>The top-level property names of the request body, as sent (a case-insensitive set); empty
    /// for trash, restore and purge.</summary>
    public IReadOnlySet<string> BodyKeys { get; }

    /// <summary>
    /// True when the write changed state. False in <see cref="IItemWriteInterceptor.AfterCommitAsync"/>
    /// for a trash of an already-trashed row and a restore of a live row. A unit test of an interceptor
    /// can build a context with <c>new ItemWriteContext(...) { Changed = false }</c>; no hook can change it.
    /// </summary>
    public bool Changed { get; init; } = true;

    /// <summary>The request body (the snapshot for a revert); null for trash, restore and purge.</summary>
    public JsonElement? Body { get; }
}
