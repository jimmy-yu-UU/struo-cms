namespace Struo.Application.Security;

/// <summary>
/// CRUD over the <c>user_sessions</c> index — one row per live cookie-session ticket key, keyed by the
/// user it belongs to. Exists because the actual ticket payload store (<c>IDistributedCache</c>) has no
/// key-scan or set operation: this is the only way to answer "every live session for user X", which
/// <c>DistributedCacheTicketStore</c> needs to keep in sync as tickets are stored/renewed/removed, and
/// <see cref="IUserSessionRevocationService"/> needs to revoke them all after a password change.
/// </summary>
public interface IUserSessionStore
{
    /// <summary>Inserts a new index row for a freshly stored ticket.</summary>
    Task RecordAsync(Guid userId, string ticketKey, DateTime createdAtUtc, DateTime expiresAtUtc, CancellationToken ct = default);

    /// <summary>Updates the expiry of the row for an existing ticket key. Returns whether a row
    /// actually matched — false means this ticket key has no index row (e.g. a session that predates
    /// this feature, or whose <see cref="RecordAsync"/> was skipped because the user id could not be
    /// read at the time). Callers that need every live session findable treat <c>false</c> as a signal
    /// to back-fill a row via <see cref="RecordAsync"/> instead.</summary>
    Task<bool> RenewAsync(string ticketKey, DateTime expiresAtUtc, CancellationToken ct = default);

    /// <summary>Deletes the row for one ticket key. A no-op when no row matches.</summary>
    Task RemoveByTicketKeyAsync(string ticketKey, CancellationToken ct = default);

    /// <summary>Deletes this user's own rows whose <c>ExpiresAt</c> is already in the past. Scoped to
    /// one user — never a full-table sweep.</summary>
    Task RemoveExpiredForUserAsync(Guid userId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Every ticket key currently indexed for this user.</summary>
    Task<IReadOnlyList<string>> ListTicketKeysForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Deletes every row for this user.</summary>
    Task RemoveAllForUserAsync(Guid userId, CancellationToken ct = default);
}
