namespace Struo.Domain.Seo;

public interface ISeoMeta
{
    string? SeoTitle { get; set; }
    string? SeoMetaDescription { get; set; }
    long? SeoOgImageId { get; set; }   // FK to files in Phase 5; scalar for now
}
