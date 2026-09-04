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
    internal static IReadOnlyList<CollectionMetadata> Collections() =>
        [Article(), Category(), Tag(), File(), Parent(), Child(), ParentChild()];

    internal static IMetadataProvider Provider() => new FakeProvider(Collections());
    internal static IEntityRegistry Registry() => new FakeRegistry();
    internal static IM2MDescriptorSource M2MSource() => new FakeM2MDescriptorSource();

    private static CollectionMetadata Article() => new()
    {
        Name = "article",
        Label = "Article",
        // Revisions = true so the schema-shape gate (GraphQlSchemaTests) sees the per-collection
        // articleRevisions/articleRevision query fields; Category is left non-revisioned so other
        // tests can still prove those fields are absent for a non-opted-in collection.
        Revisions = true,
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

    // --- parent/child/parentChild: a payload-carrying M2M fixture for JunctionPayloadGraphQlTests.
    // "parent.children" is the M2M relation WITH junction payload (note: Text, secret: Text/Hidden);
    // "parent.plainChildren" targets the same collection WITHOUT payload, proving the additive
    // <rel>Links/<Rel>Link/<Rel>Junction/<Rel>LinkInput shapes are generated only for the former.
    // Wired to FakeM2MDescriptorSource below (IM2MDescriptorSource), mirroring how RelationshipGraph
    // derives M2MDescriptor.JunctionPayload from the junction collection's own metadata in production. ---

    private static CollectionMetadata Parent() => new()
    {
        Name = "parent",
        Label = "Parent",
        FieldGroups = [],
        Fields =
        [
            new FieldMetadata
            {
                Name = "name", Label = "Name", Interface = FieldInterface.Text,
                Required = true, Sort = 1,
            },
        ],
        Relations =
        [
            new RelationMetadata
            {
                Name = "children", Label = "Children", Kind = RelationKind.ManyToMany,
                TargetCollection = "child", Interface = RelationInterface.TagSelect,
                DisplayTemplate = "{Name}",
            },
            new RelationMetadata
            {
                Name = "plainChildren", Label = "Plain Children", Kind = RelationKind.ManyToMany,
                TargetCollection = "child", Interface = RelationInterface.TagSelect,
                DisplayTemplate = "{Name}",
            },
            // HasPayload=true (JunctionPayload has one entry) but that entry is Hidden — the
            // CRITICAL regression fixture: a zero-EXPOSABLE-field junction must not emit a
            // zero-field <Rel>Junction (HotChocolate rejects that at schema-build time), so this
            // relation must get NONE of the additive Link/Junction/Links/LinkInput artefacts,
            // exactly like a payload-free relation.
            new RelationMetadata
            {
                Name = "secretChildren", Label = "Secret Children", Kind = RelationKind.ManyToMany,
                TargetCollection = "child", Interface = RelationInterface.TagSelect,
                DisplayTemplate = "{Name}",
            },
        ],
    };

    private static CollectionMetadata Child() => new()
    {
        Name = "child",
        Label = "Child",
        FieldGroups = [],
        Fields =
        [
            new FieldMetadata
            {
                Name = "name", Label = "Name", Interface = FieldInterface.Text,
                Required = true, Sort = 1,
            },
        ],
        // Two payload-free M2M relations so nested-relation tests have real, DISJOINT relations to
        // select: `childrenLinks { node { tags { name } } }` proves BuildDeep recurses INTO node's
        // own sub-selection; selecting `children { categories { ... } }` AND
        // `childrenLinks { node { tags { ... } } }` together at the same level proves the two
        // selections' nested Deep trees MERGE (categories + tags) rather than one clobbering the
        // other.
        Relations =
        [
            new RelationMetadata
            {
                Name = "tags", Label = "Tags", Kind = RelationKind.ManyToMany,
                TargetCollection = "tag", Interface = RelationInterface.TagSelect,
                DisplayTemplate = "{Name}",
            },
            new RelationMetadata
            {
                Name = "categories", Label = "Categories", Kind = RelationKind.ManyToMany,
                TargetCollection = "category", Interface = RelationInterface.TagSelect,
                DisplayTemplate = "{Name}",
            },
        ],
    };

    // The junction collection backing "parent.children" — a real [CmsCollection]-shaped entity in
    // production, hand-built here. Only its payload columns (note/secret) need FieldMetadata: they
    // are what CollectionSchemaBuilder.JunctionFieldInterface looks up by name.
    private static CollectionMetadata ParentChild() => new()
    {
        Name = "parentChild",
        Label = "Parent Child",
        FieldGroups = [],
        Fields =
        [
            new FieldMetadata
            {
                Name = "note", Label = "Note", Interface = FieldInterface.Text, Sort = 1,
            },
            new FieldMetadata
            {
                Name = "secret", Label = "Secret", Interface = FieldInterface.Text, Hidden = true, Sort = 2,
            },
        ],
    };

    // NOTE: deliberately NO CollectionMetadata for "parentSecretChild" is registered here (see
    // FakeM2MDescriptorSource below) — it only needs to exist as a name on the M2MDescriptor for
    // the CRITICAL regression fixture ("secretChildren", whose only JunctionPayloadField is
    // Hidden). Registering it as a real collection whose only field is Hidden would itself produce
    // a DIFFERENT zero-field object (a zero-writable-field ParentSecretChildCreateInput) — an
    // unrelated crash this fixture must not also trigger. JunctionPayloadField.Hidden already short-
    // circuits CollectionSchemaBuilder.ExposableJunctionFields before it would ever look the
    // junction collection's metadata up, so no registration is needed for the scenario under test.

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

    private sealed class ParentPoco
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private sealed class ChildPoco
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    // Backs BOTH the "parentChild" collection's EntityDescriptor AND
    // FakeM2MDescriptorSource's M2MDescriptor.JunctionType below — the same CLR type production
    // uses for a junction collection (RelationshipGraph reflects JunctionPayloadField.Property
    // against this exact Type to resolve each payload field's CLR type for NumberSdl).
    private sealed class ParentChildPoco
    {
        public Guid Id { get; set; }
        public Guid ParentId { get; set; }
        public Guid ChildId { get; set; }
        public string? Note { get; set; }
        public string? Secret { get; set; }
    }

    // Exists purely to give M2MDescriptor.JunctionType (the "secretChildren" CRITICAL-fix
    // fixture, below) a real CLR type — unreflected in practice, since its only payload field is
    // Hidden and ExposableJunctionFields short-circuits before ever reaching JunctionType.GetProperty.
    private sealed class ParentSecretChildPoco
    {
        public Guid Id { get; set; }
        public Guid ParentId { get; set; }
        public Guid ChildId { get; set; }
        public string? OnlyHidden { get; set; }
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
                ["parent"] = new EntityDescriptor(
                    typeof(ParentPoco),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "Id",
                        ["name"] = "Name",
                    },
                    "Id"),
                ["child"] = new EntityDescriptor(
                    typeof(ChildPoco),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "Id",
                        ["name"] = "Name",
                    },
                    "Id"),
                ["parentChild"] = new EntityDescriptor(
                    typeof(ParentChildPoco),
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["id"] = "Id",
                        ["note"] = "Note",
                        ["secret"] = "Secret",
                    },
                    "Id"),
                // No "parentSecretChild" entry: that collection is deliberately not registered
                // with FakeProvider either (see the NOTE above Parent()'s M2M relations) — the
                // registry lookup path is exercised by "parentChild" above.
            };

        public EntityDescriptor? Get(string collection) =>
            ByCollection.TryGetValue(collection, out var d) ? d : null;
    }

    // IM2MDescriptorSource fake: only "parent" carries M2M descriptors, and only "children" and
    // "secretChildren" have one (HasPayload=true) — "plainChildren" deliberately has none, so
    // CollectionSchemaBuilder's `DescriptorFor(...)` lookup misses for it and no
    // Link/Junction/LinkInput types are generated (the payload-free-relation pin).
    // "secretChildren"'s HasPayload=true but its only JunctionPayload entry is Hidden — the
    // CRITICAL regression fixture: ExposableJunctionFields(descriptor) must be empty for it too,
    // so it must ALSO get none of the additive artefacts (a zero-field <Rel>Junction would
    // otherwise abort the whole schema build).
    private sealed class FakeM2MDescriptorSource : IM2MDescriptorSource
    {
        private static readonly IReadOnlyList<M2MDescriptor> ParentDescriptors =
        [
            new M2MDescriptor(
                RelationName: "children",
                TargetCollection: "child",
                JunctionType: typeof(ParentChildPoco),
                ParentFkProperty: "ParentId",
                TargetFkProperty: "ChildId",
                SortProperty: null,
                JunctionCollection: "parentChild",
                JunctionPayload:
                [
                    new JunctionPayloadField("note", "Note", Hidden: false),
                    new JunctionPayloadField("secret", "Secret", Hidden: true),
                ]),
            new M2MDescriptor(
                RelationName: "secretChildren",
                TargetCollection: "child",
                JunctionType: typeof(ParentSecretChildPoco),
                ParentFkProperty: "ParentId",
                TargetFkProperty: "ChildId",
                SortProperty: null,
                JunctionCollection: "parentSecretChild",
                JunctionPayload:
                [
                    new JunctionPayloadField("onlyHidden", "OnlyHidden", Hidden: true),
                ]),
        ];

        public IReadOnlyList<M2MDescriptor> M2MDescriptors(string collection) =>
            string.Equals(collection, "parent", StringComparison.OrdinalIgnoreCase) ? ParentDescriptors : [];
    }
}
