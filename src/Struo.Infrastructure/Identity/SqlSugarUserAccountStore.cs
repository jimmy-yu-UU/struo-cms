// src/Struo.Infrastructure/Identity/SqlSugarUserAccountStore.cs
using SqlSugar;
using Struo.Application.Security;

namespace Struo.Infrastructure.Identity;

public sealed class SqlSugarUserAccountStore(ISqlSugarClient db) : IUserAccountStore
{
    public async Task<Guid> CreateAsync(
        string email, string passwordHash, string? name, CancellationToken ct = default)
    {
        var id = Guid.CreateVersion7();
        // Insertable(entity) IS InsertByObject, so AuditAop stamps CreatedAt/CreatedBy/UpdatedAt/
        // UpdatedBy here without help — unlike the column-scoped updates below.
        await db.Insertable(new User
        {
            Id = id, Email = email, Password = passwordHash, Name = name, IsActive = true,
        }).ExecuteCommandAsync(ct);
        return id;
    }

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) =>
        db.Queryable<User>().Where(u => u.Id == id).AnyAsync(ct);

    public async Task<UserProfile?> FindProfileAsync(Guid id, CancellationToken ct = default)
    {
        var u = await db.Queryable<User>().Where(x => x.Id == id).FirstAsync(ct);
        return u is null ? null : new UserProfile(u.Id, u.Email, u.Name);
    }

    public Task<bool> SetPasswordAsync(
        Guid id, string passwordHash, Guid? actor, DateTime nowUtc, CancellationToken ct = default) =>
        ApplyAsync(id, actor, nowUtc, u => new User { Password = passwordHash }, ct);

    public Task<bool> SetAccessTokenAsync(
        Guid id, string tokenHash, Guid? actor, DateTime nowUtc, CancellationToken ct = default) =>
        ApplyAsync(id, actor, nowUtc,
            u => new User { AccessToken = tokenHash, AccessTokenCreatedAt = nowUtc, AccessTokenLastUsedAt = null },
            ct);

    public Task<bool> ClearAccessTokenAsync(
        Guid id, Guid? actor, DateTime nowUtc, CancellationToken ct = default) =>
        ApplyAsync(id, actor, nowUtc, u => new User { AccessToken = null }, ct);

    /// <summary>
    /// Applies a column-scoped credential update with the audit trio chained on, returning whether a
    /// row matched. These are <c>SetColumns</c> updates, not <c>UpdateByObject</c>, so
    /// <see cref="Struo.Infrastructure.Persistence.AuditAop"/> never fires for them and they are off
    /// <c>SqlSugarItemRepository</c>'s version-bump path — the stamping has to be explicit here or
    /// credential changes leave no record of who made them and silently escape optimistic concurrency.
    /// <para>
    /// Both sets use the <c>SetColumns(u =&gt; new User{...})</c> member-init overload rather than the
    /// <c>u =&gt; u.Col == value</c> equality overload, for the reason
    /// <c>SqlSugarItemRepository.SoftDeleteGenericAsync</c> documents at length: a null
    /// (<c>UpdatedBy</c>, or a revoked <c>AccessToken</c>) is then typed from the underlying CLR type
    /// instead of being sent as an untyped null, which PostgreSQL rejects against a <c>uuid</c> column
    /// (42804). <c>Version = u.Version + 1</c> resolves to the SQL fragment
    /// <c>version = version + 1</c> — increment-only, no compare-and-swap, because these endpoints
    /// accept no client version and so must never fail on one.
    /// </para>
    /// </summary>
    private async Task<bool> ApplyAsync(
        Guid id,
        Guid? actor,
        DateTime nowUtc,
        System.Linq.Expressions.Expression<Func<User, User>> columns,
        CancellationToken ct)
    {
        var affected = await db.Updateable<User>()
            .SetColumns(columns)
            .SetColumns(u => new User { UpdatedAt = nowUtc, UpdatedBy = actor, Version = u.Version + 1 })
            .Where(u => u.Id == id)
            .ExecuteCommandAsync(ct);
        return affected > 0;
    }
}
