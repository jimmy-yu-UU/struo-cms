// src/Struo.Api/Auth/IFileAccessPolicy.cs
namespace Struo.Api.Auth;

/// <summary>
/// Owns every RBAC decision for the "file" collection (the media library). Mutations there go
/// through the dedicated file storage pipeline rather than the generic ItemService, so
/// FilesController must enforce these checks itself instead of getting them "for free" the way every
/// other collection does — otherwise any authenticated caller (incl. a role-less SSO user) could
/// upload or delete any file. This also absorbs the permission-related dependencies that used
/// to sit directly on FilesController's constructor.
/// </summary>
public interface IFileAccessPolicy
{
    bool CanWrite();

    bool CanDelete();

    /// <summary>
    /// May the current caller read a NON-published file? Requires both (a) an authenticated identity
    /// and (b) a <c>CanWrite("file")</c> grant. See
    /// <see cref="FileAccessPolicy.CanReadUnpublished"/> for the full rationale.
    /// </summary>
    bool CanReadUnpublished();
}
