// src/Struo.Application/Query/Write/MembershipWriteInterceptors.cs
using Struo.Application.Metadata;
using Struo.Application.Security;

namespace Struo.Application.Query;

/// <summary>Reads the id properties of a junction row, before and after a write.</summary>
internal static class JunctionRowReader
{
    /// <summary>The value of <paramref name="property"/> on the row as stored before the write (the
    /// pre-merge copy of an update, else the entity itself), and on the entity the write produced.</summary>
    internal static IReadOnlyCollection<Guid> Ids(
        IEntityRegistry registry, ItemWriteContext context, string property)
    {
        var descriptor = registry.Get(context.Meta.Name)!;
        var accessor = descriptor.Properties.GetValueOrDefault(property);
        var ids = new HashSet<Guid>();
        foreach (var row in new[] { context.Existing, context.Entity })
            if (row is not null && accessor?.GetValue(row) is Guid id && id != Guid.Empty) ids.Add(id);
        return ids;
    }

    internal static Guid? Before(IEntityRegistry registry, ItemWriteContext context, string property)
    {
        var accessor = registry.Get(context.Meta.Name)!.Properties.GetValueOrDefault(property);
        return accessor?.GetValue(context.Existing ?? context.Entity) as Guid?;
    }
}

/// <summary>
/// Writes to the hidden <c>userRole</c> junction, which the item API reaches without the user form. Like
/// the form's role field it is governed by write on <c>user</c>; only a super-admin can touch a membership
/// of a super-admin role or of a super-admin account, and the last active super-admin account cannot lose
/// its membership (a conflict that rolls the write back).
/// </summary>
public sealed class UserRoleWriteInterceptor(IEntityRegistry registry, SuperAdminGuard guard)
    : IItemWriteInterceptor, IInTransactionPreWrite
{
    private const string RoleId = "RoleId";
    private const string UserId = "UserId";

    // The users of the write in flight that belonged to a super-admin role before it; read inside the
    // transaction, after the super-admin role rows are locked.
    private IReadOnlySet<Guid> superAdminBefore = new HashSet<Guid>();

    public bool AppliesTo(string collection) =>
        string.Equals(collection, RbacCollections.UserRole, StringComparison.OrdinalIgnoreCase);

    public async Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
    {
        await guard.RequireForRolesAsync(
            JunctionRowReader.Ids(registry, context, RoleId), "change the members of a super-admin role", ct);
        foreach (var userId in JunctionRowReader.Ids(registry, context, UserId))
            await guard.RequireForUserAsync(userId, "change a super-admin account", ct);
    }

    async Task IInTransactionPreWrite.BeforeWriteInTransactionAsync(ItemWriteContext context, CancellationToken ct)
    {
        await guard.LockSuperAdminRolesAsync(ct);
        await guard.RequireForRolesAsync(
            JunctionRowReader.Ids(registry, context, RoleId), "change the members of a super-admin role", ct);
        var users = JunctionRowReader.Ids(registry, context, UserId);
        foreach (var userId in users)
            await guard.RequireForUserAsync(userId, "change a super-admin account", ct);
        superAdminBefore = await guard.SuperAdminStatusBeforeAsync(users, ct);
    }

    public async Task InTransactionAsync(ItemWriteContext context, CancellationToken ct)
    {
        await guard.EnsureSuperAdminStatusUnchangedAsync(
            superAdminBefore, JunctionRowReader.Ids(registry, context, UserId), ct);
        if (context.Operation == ItemWriteOperation.Create) return;
        if (JunctionRowReader.Before(registry, context, RoleId) is not { } roleId
            || !await guard.IsSuperAdminRoleAsync(roleId, ct)) return;

        await guard.EnsureActiveSuperAdminAsync(ct);
    }
}

/// <summary>Writes to the hidden <c>permission</c> junction, governed by write on <c>role</c>; the grants
/// of a super-admin role and of the <c>public</c> role are a super-admin's to change.</summary>
public sealed class PermissionWriteInterceptor(IEntityRegistry registry, SuperAdminGuard guard) : IItemWriteInterceptor
{
    public bool AppliesTo(string collection) =>
        string.Equals(collection, RbacCollections.Permission, StringComparison.OrdinalIgnoreCase);

    public Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct) =>
        guard.RequireToChangeRolesAsync(JunctionRowReader.Ids(registry, context, "RoleId"), grants: true, ct);
}
