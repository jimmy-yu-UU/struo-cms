namespace Struo.Domain.Seo;

public interface ISeoMeta
{
    string? SeoTitle { get; set; }
    string? SeoMetaDescription { get; set; }
    Guid? SeoOgImageId { get; set; }   // FK to the File collection (Phase 5)
}
