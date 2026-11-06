// src/Struo.Application/Query/Write/UserWriteInterceptor.cs
using System.Text.Json;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// The <c>user</c> collection's write rules. An email in the body is stored lowercase and must be unique
/// case-insensitively, matching <c>UsersController</c> and the external-login store. Only a super-admin can
/// change, delete or restore an account that belongs to a super-admin role, or assign such a role; the
/// last active account of the super-admin roles can be neither deactivated, stripped of its role nor
/// deleted (a conflict that rolls the write back). Deleting a user clears that user's sessions: the
/// per-request liveness check only catches a deleted user's session on its next use, so a session never
/// presented again would otherwise linger. Placed on the item write path, not in a controller, so REST and
/// the GraphQL <c>deleteUser</c> mutation are both covered.
/// </summary>
public sealed class UserWriteInterceptor(
    IEntityRegistry registry, IUserSessionRevocationService sessionRevocation, SuperAdminGuard guard,
    IUserCredentialStore? credentials = null)
    : IItemWriteInterceptor, IInTransactionPreWrite
{
    private const string EmailField = "email";
    private const string EmailProperty = "Email";
    private const string RolesField = "roles";
    private const string IsActiveField = "isActive";

    // Users that were an active super-admin before the write in flight, so the transaction can check that
    // someone still is afterwards. Filled and consumed inside the write transaction.
    private readonly HashSet<Guid> activeSuperAdminsBefore = [];

    // For a caller who is not a super-admin: the users of the write in flight that belonged to a super-admin
    // role before it (none: the transaction refuses a super-admin target). It is compared with the state the
    // write left.
    private readonly Dictionary<Guid, IReadOnlySet<Guid>> superAdminBefore = [];

    public bool AppliesTo(string collection) =>
        string.Equals(collection, UserCollection.Name, StringComparison.OrdinalIgnoreCase);

    public async Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
    {
        await GuardSuperAdminsAsync(context, ct);
        await NormalizeEmailAsync(context, ct);
    }

    // The early answer, before a transaction is opened; the transaction repeats it on locked, current rows.
    private async Task GuardSuperAdminsAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation == ItemWriteOperation.Create || !Guid.TryParse(context.Id, out var userId)) return;

        await guard.RequireForUserAsync(userId, "change a super-admin account", ct);
        if (context.Operation is ItemWriteOperation.Update or ItemWriteOperation.Revert)
            await guard.RequireForRolesAsync(RequestedRoleIds(context), "assign a super-admin role", ct);
    }

    // Everything a rule reads about super-admin membership is read here, inside the transaction, after the
    // super-admin role rows are locked: the state read is the state the write changes.
    async Task IInTransactionPreWrite.BeforeWriteInTransactionAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation == ItemWriteOperation.Create || !Guid.TryParse(context.Id, out var userId)) return;

        var update = context.Operation is ItemWriteOperation.Update or ItemWriteOperation.Revert;
        var couldRemove = CouldRemoveFromSuperAdmins(context);
        if (!couldRemove && !(update && !guard.CallerIsSuperAdmin)) return;

        await guard.LockSuperAdminRolesAsync(ct);
        await guard.RequireForUserAsync(userId, "change a super-admin account", ct);
        if (update)
        {
            await guard.RequireForRolesAsync(RequestedRoleIds(context), "assign a super-admin role", ct);
            superAdminBefore[userId] = await guard.SuperAdminStatusBeforeAsync([userId], ct);
        }

        if (couldRemove && await guard.IsActiveSuperAdminAsync(userId, ct)) activeSuperAdminsBefore.Add(userId);
    }

    private static bool CouldRemoveFromSuperAdmins(ItemWriteContext context) =>
        context.Operation is ItemWriteOperation.Trash or ItemWriteOperation.Purge
        || context.BodyKeys.Contains(IsActiveField) || context.BodyKeys.Contains(RolesField);

    // The role ids the body assigns, as bare ids or as objects carrying an `id`; anything else is left to the
    // relation sync's own validation.
    private static IReadOnlyCollection<Guid> RequestedRoleIds(ItemWriteContext context)
    {
        if (context.Body is not { } body || !JsonBodyUtil.TryGetRelationArray(body, RolesField, out var roles)) return [];

        var ids = new List<Guid>();
        foreach (var element in roles.EnumerateArray())
        {
            var raw = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("id", out var id) ? id : element;
            if (raw.ValueKind == JsonValueKind.String && Guid.TryParse(raw.GetString(), out var roleId)) ids.Add(roleId);
        }
        return ids;
    }

    private async Task NormalizeEmailAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation is not (ItemWriteOperation.Update or ItemWriteOperation.Revert)
            || !context.BodyKeys.Contains(EmailField)) return;

        var descriptor = registry.Get(context.Meta.Name)!;
        var email = descriptor.Properties.GetValueOrDefault(EmailProperty);
        if (email is not { CanWrite: true } || email.GetValue(context.Entity) is not string raw) return;

        var normalized = EmailAddress.Normalize(raw);
        email.SetValue(context.Entity, normalized);
        if (credentials is null || normalized.Length == 0) return;

        var self = descriptor.Properties.GetValueOrDefault(descriptor.IdProperty)?.GetValue(context.Entity) as Guid?;
        var holder = await credentials.FindByEmailAsync(normalized, ct);
        if (holder is not null && holder.Id != self)
            throw new ConflictException("Email already in use.");
    }

    public async Task InTransactionAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (!Guid.TryParse(context.Id, out var userId)) return;

        // The relations are synchronized by now: whatever the body looked like, a caller who is not a
        // super-admin must not have changed who belongs to a super-admin role.
        if (superAdminBefore.Remove(userId, out var before))
            await guard.EnsureSuperAdminStatusUnchangedAsync(before, [userId], ct);

        if (activeSuperAdminsBefore.Remove(userId))
            await guard.EnsureActiveSuperAdminAsync(ct);
    }

    // Runs once a delete took effect (a user row is never soft-deleted, so every delete is a purge); it stays
    // committed whatever happens here. A revocation failure surfaces as a distinct, client-safe error
    // rather than an indistinguishable success.
    public async Task AfterCommitAsync(ItemWriteContext context, CancellationToken ct)
    {
        if (context.Operation is not (ItemWriteOperation.Trash or ItemWriteOperation.Purge)) return;
        if (!Guid.TryParse(context.Id, out var userId)) return;

        try
        {
            await sessionRevocation.RevokeAllForUserAsync(userId, ct);
        }
        catch (Exception ex)
        {
            throw new SessionRevocationFailedException(
                "The user was deleted, but revoking their existing sessions failed. " +
                "Some sessions may still be active.", ex);
        }
    }
}
