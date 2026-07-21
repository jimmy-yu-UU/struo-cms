namespace Struo.Infrastructure.Metadata;

/// <summary>
/// The framework's own persisted entities. Some are also <c>[CmsCollection]</c> (and thus already
/// discovered by scanning); this list guarantees the non-collection identity tables are created too.
/// The <see cref="EntityTypeCollector"/> unions and de-duplicates against scanned types.
/// </summary>
public static class FrameworkEntityTypes
{
    public static readonly IReadOnlyList<Type> All =
    [
        typeof(Struo.Infrastructure.Localization.Language),
        typeof(Struo.Infrastructure.Files.File),
        typeof(Struo.Infrastructure.Files.FileTranslation),
        typeof(Struo.Infrastructure.Identity.User),
        typeof(Struo.Infrastructure.Identity.Role),
        typeof(Struo.Infrastructure.Identity.Permission),
        typeof(Struo.Infrastructure.Identity.UserRole),
        typeof(Struo.Infrastructure.Revisions.Revision),
        typeof(Struo.Infrastructure.Settings.SiteSettings),
    ];
}
