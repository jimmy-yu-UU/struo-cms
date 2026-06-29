using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Seo;

/// <summary>
/// Base for translation sidecars that carry SEO. SEO is declared ONLY on the sidecar (per-locale);
/// the parent-side ISeoMeta convention is retired (Phase 5.6 §2). Translation entities are POCOs
/// with no other base, so this base is usable there. Carries [CmsField] (a Domain attribute — §2 ok)
/// but NO [SugarColumn] (Domain stays package-free).
/// </summary>
public abstract class SeoTranslation
{
    [CmsField(Label = "SEO Title", Interface = FieldInterface.Text, Group = "SEO", Sort = 10)]
    public string? SeoTitle { get; set; }

    [CmsField(Label = "SEO Description", Interface = FieldInterface.Textarea, Group = "SEO", Sort = 11)]
    public string? SeoMetaDescription { get; set; }

    [CmsField(Label = "OG Image", Interface = FieldInterface.Image, Group = "SEO", Sort = 12)]
    public Guid? SeoOgImageId { get; set; }
}
