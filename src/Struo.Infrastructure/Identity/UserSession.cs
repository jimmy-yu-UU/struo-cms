using SqlSugar;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Identity;

/// <summary>
/// Index of live cookie sessions: one row per <c>DistributedCacheTicketStore</c> (<c>src/Struo.Api/
/// Auth/DistributedCacheTicketStore.cs</c>) cache key, keyed by the user it belongs to. An internal
/// framework table — NOT a <c>[CmsCollection]</c> (like <see cref="Struo.Infrastructure.Revisions.Revision"/>
/// and <see cref="Struo.Infrastructure.Settings.SiteSettings"/>), so it is never browsable/CRUD-able
/// through the generic item API. It exists purely because the actual ticket payload store
/// (<c>IDistributedCache</c> — Redis in production) has no key-scan or set operation, so there is no
/// other way to find "every live session for user X" — needed both to revoke them all after a password
/// change and to know which rows are stale enough to sweep at that user's next login. Rows are deleted
/// outright on logout/revocation/sweep and never soft-deleted; not
/// <c>AuditableEntity</c>/<c>IAuditable</c>/<c>ISoftDeletable</c>.
/// </summary>
[SugarTable("user_sessions")]
[SugarIndex("ix_user_sessions_userid", nameof(UserId), OrderByType.Asc)]
public sealed class UserSession
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    public Guid UserId { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_user_sessions_ticketkey"])]
    public string TicketKey { get; set; } = "";

    // Compared against UTC instants (ExpiresAt < nowUtc) and a new framework table, so both get the
    // zone-aware shape per ColumnShape's own convention — see its class doc.
    [ColumnShape(ColumnShape.TimestampWithTimeZone)] public DateTime CreatedAt { get; set; }
    [ColumnShape(ColumnShape.TimestampWithTimeZone)] public DateTime ExpiresAt { get; set; }
}
