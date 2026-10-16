// src/Struo.Application/Query/Write/UserEmailRules.cs
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Generic-API write rules for the <c>User</c> collection's email: stored lowercase and unique
/// case-insensitively, matching <c>UsersController</c> and the external-login store.
/// </summary>
internal sealed class UserEmailRules(IUserCredentialStore? credentials)
{
    internal const string Collection = "User";
    internal const string EmailField = "email";
    private const string EmailProperty = "Email";

    public static bool AppliesTo(string collection) =>
        string.Equals(collection, Collection, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lowercases the entity's email in place and rejects an address another user already holds.</summary>
    public async Task NormalizeAndCheckAsync(EntityDescriptor descriptor, object entity, CancellationToken ct)
    {
        var email = descriptor.Properties.GetValueOrDefault(EmailProperty);
        if (email is not { CanWrite: true } || email.GetValue(entity) is not string raw) return;

        var normalized = EmailAddress.Normalize(raw);
        email.SetValue(entity, normalized);
        if (credentials is null || normalized.Length == 0) return;

        var self = descriptor.Properties.GetValueOrDefault(descriptor.IdProperty)?.GetValue(entity) as Guid?;
        var holder = await credentials.FindByEmailAsync(normalized, ct);
        if (holder is not null && holder.Id != self)
            throw new ConflictException("Email already in use.");
    }
}
