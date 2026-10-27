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
public sealed class ItemWriteContext(
    ItemWriteOperation operation, CollectionMetadata meta, object entity, string? id,
    IReadOnlySet<string> bodyKeys, JsonElement? body)
{
    public ItemWriteOperation Operation { get; } = operation;

    public CollectionMetadata Meta { get; } = meta;

    /// <summary>
    /// The entity being written, mutable until the transaction runs: for create, update and revert the
    /// request body merged into it; for trash, restore and purge the existing row. After the row is
    /// written it is the stored entity.
    /// </summary>
    public object Entity { get; internal set; } = entity;

    /// <summary>The item id; null for a create until the row is inserted.</summary>
    public string? Id { get; internal set; } = id;

    /// <summary>The camelCase names of the keys the request body carries; empty for trash, restore and purge.</summary>
    public IReadOnlySet<string> BodyKeys { get; } = bodyKeys;

    /// <summary>The request body (the snapshot for a revert); null for trash, restore and purge.</summary>
    public JsonElement? Body { get; } = body;
}
