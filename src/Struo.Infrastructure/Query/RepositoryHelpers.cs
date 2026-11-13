// src/Struo.Infrastructure/Query/RepositoryHelpers.cs
using SqlSugar;
using Struo.Application.Metadata;

namespace Struo.Infrastructure.Query;

internal static class RepositoryHelpers
{
    public static EntityDescriptor Descriptor(IEntityRegistry registry, string collection) =>
        registry.Get(collection) ?? throw new InvalidOperationException($"Unknown collection '{collection}'.");

    // Inside a transaction a read is a locking read (FOR UPDATE, or UPDLOCK on SQL Server): it sees the
    // latest committed row even where the transaction's snapshot is older, and a concurrent change waits.
    // SQLite has a single writer and takes no lock clause.
    public static ISugarQueryable<T> Locked<T>(ISqlSugarClient db, ISugarQueryable<T> query) =>
        db.Ado.IsAnyTran() && db.CurrentConnectionConfig.DbType != DbType.Sqlite
            ? query.TranLock(DbLockType.Wait)
            : query;

    /// <summary>
    /// Converts a string ID to the PK property type. Handles Guid and all IConvertible types.
    /// Delegates entirely to the Application-layer twin so any unparseable id surfaces as a
    /// mappable <see cref="Struo.Domain.Query.QueryException"/> (-&gt; HTTP 400) instead of a raw FormatException/
    /// ArgumentException that <c>StruoExceptionHandler.Map</c> cannot map and masks as a 500.
    /// </summary>
    public static object ConvertId(string id, EntityDescriptor d)
    {
        var idType = d.EntityType.GetProperty(d.IdProperty)!.PropertyType;
        return Struo.Application.Query.IdParsing.ParseTo(id, idType);
    }
}
