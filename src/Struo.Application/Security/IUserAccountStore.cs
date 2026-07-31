// src/Struo.Application/Security/IUserAccountStore.cs
namespace Struo.Application.Security;

/// <summary>The subset of a user row the admin surface displays. No credential material — those stay
/// behind <see cref="IUserCredentialStore"/>.</summary>
public sealed record UserProfile(Guid Id, string Email, string? Name);

/// <summary>
/// User-account administration: creating a user and mutating credential columns. Distinct from
/// <see cref="IUserCredentialStore"/>, which is the read path the AUTHENTICATION pipeline uses
/// (<c>AuthService</c>, the bearer handler) and whose stated purpose is credential lookups that keep
/// password hashes off the generic read path. Loading administration writes onto that interface would
/// widen it past what its consumers need, so they live here instead.
/// <para>
/// Every mutator below is responsible for stamping the audit trio — <c>UpdatedAt</c>/<c>UpdatedBy</c>
/// and a <c>Version</c> increment — because these writes touch specific columns rather than a whole
/// entity, which puts them outside both <c>AuditAop</c> (it hooks only
/// <c>InsertByObject</c>/<c>UpdateByObject</c>) and <c>SqlSugarItemRepository</c>'s version bump.
/// Getting that wrong leaves credential changes with no record of who made them and silently exempts
/// them from optimistic concurrency; <c>UserCredentialWriteAuditTests</c> pins the behavior.
/// </para>
/// </summary>
public interface IUserAccountStore
{
    /// <summary>Inserts a user with an already-hashed password and returns the assigned id. The
    /// caller owns uniqueness checking (and the 409 that follows from it).</summary>
    Task<Guid> CreateAsync(string email, string passwordHash, string? name, CancellationToken ct = default);

    /// <summary>Does this user exist? Used for the 404 that precedes an administrative action.</summary>
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Email/name for the signed-in-user projection. Null when the id is unknown.</summary>
    Task<UserProfile?> FindProfileAsync(Guid id, CancellationToken ct = default);

    /// <summary>Replaces the password hash. Returns false when the id matched no row (→ 404).</summary>
    Task<bool> SetPasswordAsync(
        Guid id, string passwordHash, Guid? actor, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Stores a freshly issued access-token hash, records issuance, and clears the previous
    /// last-used stamp. Returns false when the id matched no row (→ 404).</summary>
    Task<bool> SetAccessTokenAsync(
        Guid id, string tokenHash, Guid? actor, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Revokes the access token. Returns false when the id matched no row (→ 404).</summary>
    Task<bool> ClearAccessTokenAsync(Guid id, Guid? actor, DateTime nowUtc, CancellationToken ct = default);
}
