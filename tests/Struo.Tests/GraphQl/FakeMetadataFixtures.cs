// tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Hand-built (not scanner-produced) metadata for four collections — article, category, tag, file —
/// used by GraphQL schema/execution tests so they can run with no DB and no reflection over real
/// [CmsCollection] entities. Mirrors the shapes the real MetadataScanner would produce for an
/// equivalent entity set (see samples/Struo.Sample.Blog/Article.cs), including leaving translatable
/// fields (title) off the backing POCO's FieldToProperty map — on the real Article entity, Title
/// lives on the ArticleTranslation sidecar, not on Article itself, so the scanner never maps it either.
/// </summary>
internal static class FakeMetadataFixtures
{
    internal static IReadOnlyList<CollectionMetadata> Collections() => [Article(), Category(), Tag(), File()];

    internal static IMetadataProvider Provider() => new FakeProvider(Collections());
    internal static IEntityRegistry Registry() => new FakeRegistry();

    private static CollectionMetadata Article() => new()
    {
        Name = "article",
        Label = "Article",
        FieldGroups = [],
        Fields =
        [
            new FieldMetadata
            {
                Name = "title", Label = "Title", Interface = FieldInterface.Text,
                Required = true, Searchable = true, Translatable = true, Sort = 1,
            },
            new FieldMetadata
            {
                Name = "status", Label = "Status", Interface = FieldInterface.Select,
                Required = true, Sort = 2,
                Options = [new FieldOption("draft", "Draft"), new FieldOption("published", "Published")],
            },
            new FieldMetadata
            {
                Name = "publishedAt", Label = "Published At", Interface = FieldInterface.DateTime,
                Sort = 3,
            },
            new FieldMetadata
            {
                Name = "heroImageId", Label = "Hero Image", Interface = FieldInterface.Image,
                Sort = 4,
            },
            new FieldMetadata
            {
                Name = "regions", Label = "Regions", Interface = FieldInterface.MultiSelect,
                Sort = 5,
                Options = [new FieldOption("apac", "APAC"), new FieldOption("emea", "EMEA")],
            },
            new FieldMetadata
            {
                Name = "keywords", Label = "Keywords", Interface = FieldInterface.Tags,
                Sort = 6,
            },
            new FieldMetadata
            {
                Name = "attributes", Label = "Attributes", Interface = FieldInterface.Json,
                Sort = 7,
            },
            new FieldMetadata
            {
                Name = "gallery", Label = "Gallery", Interface = FieldInterface.Files,
                Sort = 8,
            },
            new FieldMetadata
            {
                Name = "faqs", Label = "FAQs", Interface = FieldInterface.Repeater,
                Sort = 9,
                Fields =
                [
                    new FieldMetadata
                    {
                        Name = "question", Label = "Question", Interface = FieldInterface.Text, Required = true,
                    },
                    new FieldMetadata
                    {
                        Name = "answer", Label = "Answer", Interface = FieldInterface.Textarea,
                    },
                ],
            },
            new FieldMetadata
            {
                Name = "body", Label = "Body", Interface = FieldInterface.RichText,
                Translatable = true, Sort = 10,
            },
            new FieldMetadata
            {
                Name = "seoOgImageId", Label = "OG Image", Interface = FieldInterface.Image,
                Translatable = true, Sort = 11,
            },
        ],
        Relations =
        [
            new RelationMetadata
            {
                Name = "category", Label = "Category", Kind = RelationKind.ManyToOne,
                TargetCollection = "category", Interface = RelationInterface.Dropdown,
                ForeignKey = "categoryId", DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull,
            },
            new RelationMetadata
            {
                Name = "tags", Label = "Tags", Kind = RelationKind.ManyToMany,
                TargetCollection = "tag", Interface = RelationInterface.TagSelect,
                DisplayTemplate = "{Name}",
            },
        ],
        Translation = new TranslationMetadata
        {
            ForeignKeyProperty = "ArticleId",
            LocaleProperty = "Locale",
            Fields = ["title", "body", "seoOgImageId"],
        },
    };

    private static CollectionMetadata Category() => new()
    {
        Name = "category",
        Label = "Category",
        FieldGroups = [],
        Fields =
        [
            new FieldMetadata
            {
                Name = "name", Label = "Name", Interface = FieldInterface.Text,
                Required = true, Searchable = true, Sort = 1,
            },
        ],
        Relations =
        [
            new RelationMetadata
            {
                Name = "parent", Label = "Parent", Kind = RelationKind.ManyToOne,
                TargetCollection = "category", Interface = RelationInterface.TreeSelect,
                ForeignKey = "parentId", DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull,
            },
            new RelationMetadata
            {
                Name = "articles", Label = "Articles", Kind = RelationKind.OneToMany,
                TargetCollection = "article", Interface = RelationInterface.RelatedList,
                DisplayTemplate = "{Title}",
            },
        ],
    };

    private static CollectionMetadata Tag() => new()
    {
        Name = "tag",
        Label = "Tag",
        FieldGroups = [],
        Fields =
        [
            new FieldMetadata
            {
                Name = "name", Label = "Name", Interface = FieldInterface.Text,
                Required = true, Searchable = true, Sort = 1,
            },
        ],
    };

    private static CollectionMetadata File() => new()
    {
        Name = "file",
        Label = "File",
        FieldGroups = [],
        Fields =
        [
            new FieldMetadata
            {
                Name = "title", Label = "Title", Interface = FieldInterface.Text, Sort = 1,
            },
            new FieldMetadata
            {
                Name = "url", Label = "URL", Interface = FieldInterface.Url, Sort = 2,
            },
            new FieldMetadata
            {
                Name = "width", Label = "Width", Interface = FieldInterface.Number, Sort = 3,
            },
            new FieldMetadata
            {
                Name = "height", Label = "Height", Interface = FieldInterface.Number, Sort = 4,
            },
        ],
    };

    // --- test POCOs: give FakeRegistry a real EntityType + FieldToProperty + IdProperty, and give
    // Number fields (width/height) a real CLR type (int) for SchemaTypeMapper/FilterInputTranslator
    // to reflect. Deliberately plain — no SqlSugar/[CmsField] attributes; the registry here is
    // hand-built, not scanner-derived. ---

    private sealed class ArticlePoco
    {
        public Guid Id { get; set; }

        // Title intentionally NOT modelled here: it is translatable and, on the real Article
        // entity, lives on the ArticleTranslation sidecar rather than on Article itself — the real
        // scanner never maps it into Article's FieldToProperty either.
        public string Status { get; set; } = "draft";
        public DateTime? PublishedAt { get; set; }
        public Guid? HeroImageId { get; set; }
        public List<string> Regions { get; set; } = [];
        public List<TagItem> Keywords { get; set; } = [];
        public string? Attributes { get; set; }
        public List<Guid> Gallery { get; set; } = [];
        public List<FaqItemPoco> Faqs { get; set; } = [];
        public Guid? CategoryId { get; set; }
    }

    private sealed class FaqItemPoco
    {
        public string Question { get; set; } = "";
        public string Answer { get; set; } = "";
    }

    private sealed class CategoryPoco
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public Guid? ParentId { get; set; }
    }

    private sealed class TagPoco
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class FilePoco
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private sealed class FakeProvider(IReadOnlyList<CollectionMetadata> all) : IMetadataProvider
    {
        public IReadOnlyList<CollectionMetadata> GetCollections() => all;

        public CollectionMetadata? GetCollection(string name) =>
            all.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeRegistry : IEntityRegistry
    {
        private static readonly IReadOnlyDictionary<string, EntityDescriptor> ByCollection =
            new Dictionary<string, EntityDescriptor>(StringComparer.OrdinalIgnoreCase)
            {
                ["article"] = new EntityDescriptor(
                    typeof(ArticlePoco),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "Id",
                        ["status"] = "Status",
                        ["publishedAt"] = "PublishedAt",
                        ["heroImageId"] = "HeroImageId",
                        ["regions"] = "Regions",
                        ["keywords"] = "Keywords",
                        ["attributes"] = "Attributes",
                        ["gallery"] = "Gallery",
                        ["faqs"] = "Faqs",
                        // Local FK for the "category" many-to-one relation — mapped even though it
                        // carries no [CmsField], mirroring MetadataScanner.ScanDescriptors.
                        ["categoryId"] = "CategoryId",
                    },
                    "Id"),
                ["category"] = new EntityDescriptor(
                    typeof(CategoryPoco),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "Id",
                        ["name"] = "Name",
                        ["parentId"] = "ParentId",
                    },
                    "Id"),
                ["tag"] = new EntityDescriptor(
                    typeof(TagPoco),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "Id",
                        ["name"] = "Name",
                    },
                    "Id"),
                ["file"] = new EntityDescriptor(
                    typeof(FilePoco),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "Id",
                        ["title"] = "Title",
                        ["url"] = "Url",
                        ["width"] = "Width",
                        ["height"] = "Height",
                    },
                    "Id"),
            };

        public EntityDescriptor? Get(string collection) =>
            ByCollection.TryGetValue(collection, out var d) ? d : null;
    }
}
