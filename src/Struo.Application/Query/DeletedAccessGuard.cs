using Struo.Application.Security;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Single implementation of the viewing-deleted permission gate. Requesting soft-deleted
/// rows (<see cref="DeletedFilter.Only"/>/<see cref="DeletedFilter.With"/>) exposes data that a
/// plain read grant does not, so it additionally requires delete permission on the collection.
/// <see cref="ItemService"/> calls it after its read check, and the REST <c>ItemsController</c> and the
/// GraphQL <c>CollectionResolvers</c> call it at their entry points, so the rule (and its message)
/// lives in exactly one place.
/// </summary>
public static class DeletedAccessGuard
{
    /// <summary>
    /// Throws <see cref="PermissionDeniedException"/> when <paramref name="deleted"/> is not
    /// <see cref="DeletedFilter.Exclude"/> and the caller lacks delete permission on
    /// <paramref name="collection"/>. No-op otherwise.
    /// </summary>
    public static void EnsureCanViewDeleted(IPermissionService permissions, string collection, DeletedFilter deleted)
    {
        if (deleted != DeletedFilter.Exclude && !permissions.CanDelete(collection))
            throw new PermissionDeniedException("Viewing deleted items requires delete permission.");
    }
}
