// src/Struo.Api/Auth/IFileAccessPolicy.cs
namespace Struo.Api.Auth;

/// <summary>
/// Owns every RBAC decision for the "file" collection (the media library). Mutations there go
/// through the dedicated file storage pipeline rather than the generic ItemService, so
/// FilesController must enforce these checks itself instead of getting them "for free" the way every
/// other collection does — otherwise any authenticated caller (incl. a role-less SSO user) could
/// upload or delete any file. This also absorbs the permission-related dependencies that used
/// to sit directly on FilesController's constructor, and is the single owner of the bearer-adopt
/// resolution sequence that <see cref="FileAccessPolicy"/> shares with
/// <see cref="PermissionResolutionMiddleware"/>.
/// </summary>
public interface IFileAccessPolicy
{
    bool CanWrite();

    bool CanDelete();

    /// <summary>
    /// May the current caller read a NON-published file? Requires both (a) an authenticated
    /// identity and (b) a genuine <c>CanRead("file")</c> grant for that identity. See
    /// <see cref="FileAccessPolicy.CanReadUnpublishedAsync"/> for the full rationale — this is a
    /// verbatim move of the logic that used to live as a private method on FilesController.
    /// </summary>
    Task<bool> CanReadUnpublishedAsync(HttpContext httpContext, CancellationToken ct);
}
