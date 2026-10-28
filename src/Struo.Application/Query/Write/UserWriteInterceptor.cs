// src/Struo.Application/Query/Write/UserWriteInterceptor.cs
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// The <c>user</c> collection's write rules. An email in the body is stored lowercase and must be unique
/// case-insensitively, matching <c>UsersController</c> and the external-login store. Deleting a user
/// clears that user's sessions: the per-request liveness check only catches a deleted user's session on
/// its next use, so a session never presented again would otherwise linger. Placed on the item write
/// path, not in a controller, so REST and the GraphQL <c>deleteUser</c> mutation are both covered.
/// </summary>
public sealed class UserWriteInterceptor(
    IEntityRegistry registry, IUserSessionRevocationService sessionRevocation, IUserCredentialStore? credentials = null)
    : IItemWriteInterceptor
{
    private const string EmailField = "email";
    private const string EmailProperty = "Email";

    public bool AppliesTo(string collection) =>
        string.Equals(collection, UserCollection.Name, StringComparison.OrdinalIgnoreCase);

    public async Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
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
