# StruoCMS Phase 1 (Metadata Core) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn `[Cms*]`-annotated entities into a single source of truth: scan them once at startup into an immutable cached registry and expose it via `GET /api/schema` and `GET /api/schema/{collection}`.

**Architecture:** Pure attributes/enums/models in Domain; `IMetadataProvider` port + `SchemaService` in Application; a reflection `MetadataScanner` + immutable `CachedMetadataProvider` in Infrastructure, built eagerly at registration (no per-request reflection); a `SchemaController` in Api.

**Tech Stack:** .NET 10, C# latest, ASP.NET Core Controllers, System.Text.Json, xUnit + AwesomeAssertions, SqlSugar (entities only).

## Global Constraints

- **Dependency rule (§2):** Domain → nothing; Application → Domain; Infrastructure → Application+Domain; Api → Application+Infrastructure; samples → Domain; only Api references samples. Framework projects never reference `samples/*`.
- **Domain purity (§2):** `Struo.Domain` has zero external package references. `[Cms*]` attributes, enums, metadata models, `ISeoMeta`, and domain exceptions are plain C# (BCL only).
- **No per-request reflection (§3, §17.6):** scanning happens once at startup; the registry is immutable and cached.
- **camelCase JSON (§1):** outbound JSON keys are camelCase; enums serialize as camelCase strings.
- **Naming decision:** schema collection & field `name` values are camelCase (`Article`→`article`, `Title`→`title`); lookups case-insensitive.
- **Deferred:** `CmsRelationAttribute` and `CmsTranslationsAttribute` are DEFINED but NOT read by the scanner this phase (relations = Phase 3; translation tables = Phase 4). The `Translatable` flag IS emitted.
- **Packages (§15):** any new package via `dotnet add package` (latest, CPM). Never hardcode versions.
- **Build:** `TreatWarningsAsErrors=true` → 0 warnings. **TDD:** failing test first.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/Struo.Domain/Metadata/Enums/FieldInterface.cs` | Full §6 input-interface catalog |
| `src/Struo.Domain/Metadata/Enums/RelationInterface.cs` | Relation interfaces |
| `src/Struo.Domain/Metadata/Enums/OnDelete.cs` | OnDelete behavior enum |
| `src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs` | Collection marker |
| `src/Struo.Domain/Metadata/Attributes/CmsFieldAttribute.cs` | Field config |
| `src/Struo.Domain/Metadata/Attributes/CmsOptionsAttribute.cs` | Select options |
| `src/Struo.Domain/Metadata/Attributes/CmsFieldGroupAttribute.cs` | UI panel declaration |
| `src/Struo.Domain/Metadata/Attributes/CmsRelationAttribute.cs` | Relation config (defined only) |
| `src/Struo.Domain/Metadata/Attributes/CmsTranslationsAttribute.cs` | Translations link (defined only) |
| `src/Struo.Domain/Metadata/Models/FieldOption.cs` | `{value,label}` |
| `src/Struo.Domain/Metadata/Models/FieldGroupMetadata.cs` | UI panel metadata |
| `src/Struo.Domain/Metadata/Models/FieldMetadata.cs` | Field metadata record |
| `src/Struo.Domain/Metadata/Models/CollectionMetadata.cs` | Collection metadata record |
| `src/Struo.Domain/Metadata/MetadataException.cs` | Scan-time validation error |
| `src/Struo.Domain/Seo/ISeoMeta.cs` | SEO contract |
| `src/Struo.Application/Metadata/IMetadataProvider.cs` | Registry port |
| `src/Struo.Application/Metadata/SchemaService.cs` | Schema use case (Phase-6 permission seam) |
| `src/Struo.Infrastructure/Metadata/MetadataScanner.cs` | Reflection scan → models |
| `src/Struo.Infrastructure/Metadata/CachedMetadataProvider.cs` | Immutable cached registry |
| `src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs` | `AddStruoMetadata` |
| `src/Struo.Api/Controllers/SchemaController.cs` | `/api/schema` endpoints |
| `src/Struo.Api/Program.cs` (modify) | Wire metadata + enum JSON converter |
| `samples/Struo.Sample.Blog/Article.cs` (modify) | Enriched sample |
| `samples/Struo.Sample.Blog/Tag.cs` | 2nd standalone collection |
| `tests/Struo.Tests/Metadata/MetadataScannerTests.cs` | Scanner happy-path |
| `tests/Struo.Tests/Metadata/MetadataValidationTests.cs` | Scanner validation |
| `tests/Struo.Tests/Metadata/CachedMetadataProviderTests.cs` | Caching/registration |
| `tests/Struo.Tests/Api/SchemaEndpointTests.cs` | Endpoint integration |

---

### Task 1: Domain metadata types (enums, attributes, models, ISeoMeta, exception)

**Files:** all under `src/Struo.Domain/Metadata/**` and `src/Struo.Domain/Seo/ISeoMeta.cs` (see File Structure).

**Interfaces:**
- Consumes: nothing.
- Produces: enums `FieldInterface`, `RelationInterface`, `OnDelete`; attributes `CmsCollectionAttribute`, `CmsFieldAttribute`, `CmsOptionsAttribute`, `CmsFieldGroupAttribute`, `CmsRelationAttribute`, `CmsTranslationsAttribute`; records `FieldOption`, `FieldGroupMetadata`, `FieldMetadata`, `CollectionMetadata`; `MetadataException`; `ISeoMeta`. Namespaces: `Struo.Domain.Metadata.Enums`, `Struo.Domain.Metadata.Attributes`, `Struo.Domain.Metadata.Models`, `Struo.Domain.Metadata`, `Struo.Domain.Seo`.

- [ ] **Step 1: Create the enums**

```csharp
// src/Struo.Domain/Metadata/Enums/FieldInterface.cs
namespace Struo.Domain.Metadata.Enums;

public enum FieldInterface
{
    Text, Textarea, RichText, Markdown, Code, Slug, Email, Url, Password, Color, Phone,
    Number, Slider, Rating,
    Boolean, Checkbox,
    Date, Time, DateTime,
    Select, MultiSelect, Radio, CheckboxGroup, Tags,
    Json, KeyValue, Repeater,
    File, Image, Files,
    Hidden, Divider, Uuid
}
```

```csharp
// src/Struo.Domain/Metadata/Enums/RelationInterface.cs
namespace Struo.Domain.Metadata.Enums;

public enum RelationInterface { Dropdown, TagSelect, TreeSelect, RelatedList }
```

```csharp
// src/Struo.Domain/Metadata/Enums/OnDelete.cs
namespace Struo.Domain.Metadata.Enums;

public enum OnDelete { Restrict, Cascade, SetNull }
```

- [ ] **Step 2: Create the attributes**

```csharp
// src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs
namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CmsCollectionAttribute(string label) : Attribute
{
    public string Label { get; } = label;
    public string? Icon { get; set; }
    public string? Group { get; set; }
    public string? DefaultDisplayField { get; set; }
}
```

```csharp
// src/Struo.Domain/Metadata/Attributes/CmsFieldAttribute.cs
using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsFieldAttribute : Attribute
{
    public string? Label { get; set; }
    public FieldInterface Interface { get; set; } = FieldInterface.Text;
    public string? Display { get; set; }
    public bool Required { get; set; }
    public bool Searchable { get; set; }
    public bool Sortable { get; set; }
    public int Sort { get; set; }
    public bool ReadOnly { get; set; }
    public bool Hidden { get; set; }
    public string? HelpText { get; set; }
    public bool Translatable { get; set; }
    public string? Group { get; set; }
}
```

```csharp
// src/Struo.Domain/Metadata/Attributes/CmsOptionsAttribute.cs
namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsOptionsAttribute(params string[] options) : Attribute
{
    // Each entry is "value:label".
    public IReadOnlyList<string> Options { get; } = options;
}
```

```csharp
// src/Struo.Domain/Metadata/Attributes/CmsFieldGroupAttribute.cs
namespace Struo.Domain.Metadata.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CmsFieldGroupAttribute(string name) : Attribute
{
    public string Name { get; } = name;
    public string? Label { get; set; }
    public int Sort { get; set; }
}
```

```csharp
// src/Struo.Domain/Metadata/Attributes/CmsRelationAttribute.cs
using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Attributes;

// Defined for the full §4 surface; NOT read by the Phase 1 scanner (relations = Phase 3).
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsRelationAttribute : Attribute
{
    public RelationInterface Interface { get; set; } = RelationInterface.Dropdown;
    public string? DisplayTemplate { get; set; }
    public string? PickerQuery { get; set; }
    public string? SortField { get; set; }
    public OnDelete OnDelete { get; set; } = OnDelete.Restrict;
    public bool Editable { get; set; } = true;
    public string? DisplayColumns { get; set; }
    public int MaxDepth { get; set; } = 1;
}
```

```csharp
// src/Struo.Domain/Metadata/Attributes/CmsTranslationsAttribute.cs
namespace Struo.Domain.Metadata.Attributes;

// Defined for the full §4 surface; NOT read by the Phase 1 scanner (translation tables = Phase 4).
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class CmsTranslationsAttribute(Type translationEntityType) : Attribute
{
    public Type TranslationEntityType { get; } = translationEntityType;
}
```

- [ ] **Step 3: Create the metadata model records**

```csharp
// src/Struo.Domain/Metadata/Models/FieldOption.cs
namespace Struo.Domain.Metadata.Models;

public sealed record FieldOption(string Value, string Label);
```

```csharp
// src/Struo.Domain/Metadata/Models/FieldGroupMetadata.cs
namespace Struo.Domain.Metadata.Models;

public sealed record FieldGroupMetadata(string Name, string? Label, int Sort);
```

```csharp
// src/Struo.Domain/Metadata/Models/FieldMetadata.cs
using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Models;

public sealed record FieldMetadata
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required FieldInterface Interface { get; init; }
    public bool Required { get; init; }
    public bool Searchable { get; init; }
    public bool Sortable { get; init; }
    public bool ReadOnly { get; init; }
    public bool Hidden { get; init; }
    public bool Translatable { get; init; }
    public int Sort { get; init; }
    public string? HelpText { get; init; }
    public string? Group { get; init; }
    public IReadOnlyList<FieldOption>? Options { get; init; }
    public bool IsSystem { get; init; }
}
```

```csharp
// src/Struo.Domain/Metadata/Models/CollectionMetadata.cs
namespace Struo.Domain.Metadata.Models;

public sealed record CollectionMetadata
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public string? Icon { get; init; }
    public string? Group { get; init; }
    public string? DefaultDisplayField { get; init; }
    public required IReadOnlyList<FieldGroupMetadata> FieldGroups { get; init; }
    public required IReadOnlyList<FieldMetadata> Fields { get; init; }
}
```

- [ ] **Step 4: Create the domain exception and ISeoMeta**

```csharp
// src/Struo.Domain/Metadata/MetadataException.cs
namespace Struo.Domain.Metadata;

public sealed class MetadataException : Exception
{
    public MetadataException(string message) : base(message) { }
}
```

```csharp
// src/Struo.Domain/Seo/ISeoMeta.cs
namespace Struo.Domain.Seo;

public interface ISeoMeta
{
    string? SeoTitle { get; set; }
    string? SeoMetaDescription { get; set; }
    long? SeoOgImageId { get; set; }   // FK to files in Phase 5; scalar for now
}
```

- [ ] **Step 5: Build the Domain project**

Run: `dotnet build src/Struo.Domain`
Expected: Build succeeded, 0 warnings. Confirm `Struo.Domain.csproj` still has no `<PackageReference>` (Domain purity).

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Domain
git commit -m "feat: add Domain metadata attributes, enums, models, and ISeoMeta"
```

---

### Task 2: Enrich the Article sample + add the Tag collection

**Files:**
- Modify: `samples/Struo.Sample.Blog/Article.cs`
- Create: `samples/Struo.Sample.Blog/Tag.cs`

**Interfaces:**
- Consumes: Task 1 attributes/enums + `ISeoMeta`.
- Produces: a `[CmsCollection]` `Article` (implements `IAuditable, ISeoMeta`) with fields `Title`/`Body`/`Status`/`PublishedAt` and field groups `Content`/`SEO`; a `[CmsCollection]` `Tag` (implements `IAuditable`) with field `Name`. Both keep their SqlSugar persistence attributes.

- [ ] **Step 1: Replace `Article.cs` with the enriched entity**

```csharp
// samples/Struo.Sample.Blog/Article.cs
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Seo;

namespace Struo.Sample.Blog;

[SugarTable("articles")]
[CmsCollection("Article", Icon = "article", Group = "Content", DefaultDisplayField = nameof(Title))]
[CmsFieldGroup("Content", Label = "Content", Sort = 1)]
[CmsFieldGroup("SEO", Label = "SEO", Sort = 2)]
public sealed class Article : IAuditable, ISeoMeta
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true,
              Searchable = true, Translatable = true, Sort = 1, Group = "Content")]
    public string Title { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Body", Interface = FieldInterface.RichText,
              Translatable = true, Sort = 2, Group = "Content")]
    public string? Body { get; set; }

    [CmsField(Label = "Status", Interface = FieldInterface.Select, Sort = 3, Group = "Content")]
    [CmsOptions("draft:Draft", "published:Published")]
    public string Status { get; set; } = "draft";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Published At", Interface = FieldInterface.DateTime, Sort = 4, Group = "Content")]
    public DateTime? PublishedAt { get; set; }

    // ISeoMeta — SEO field group auto-applied by the scanner (no [CmsField] here by design)
    [SugarColumn(IsNullable = true)] public string? SeoTitle { get; set; }
    [SugarColumn(IsNullable = true)] public string? SeoMetaDescription { get; set; }
    [SugarColumn(IsNullable = true)] public long? SeoOgImageId { get; set; }

    // IAuditable
    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? UpdatedBy { get; set; }
}
```

- [ ] **Step 2: Create `Tag.cs`**

```csharp
// samples/Struo.Sample.Blog/Tag.cs
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("tags")]
[CmsCollection("Tag", Icon = "tag", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Tag : IAuditable
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? UpdatedBy { get; set; }
}
```

- [ ] **Step 3: Build the solution**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings. (Existing `AuditAopTests` still compile; the added nullable columns don't affect them.)

- [ ] **Step 4: Commit**

```bash
git add samples/Struo.Sample.Blog
git commit -m "feat: annotate Article sample and add Tag collection for metadata"
```

---

### Task 3: MetadataScanner — happy path (TDD)

**Files:**
- Create: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs`
- Test: `tests/Struo.Tests/Metadata/MetadataScannerTests.cs`

**Interfaces:**
- Consumes: Task 1 Domain types; Task 2 `Article`/`Tag`.
- Produces:
  - `static IReadOnlyList<CollectionMetadata> MetadataScanner.Scan(params Assembly[] assemblies)`
  - `static IReadOnlyList<CollectionMetadata> MetadataScanner.ScanTypes(IEnumerable<Type> types)`
  - Field-name and collection-name values are camelCase; option-type set = `{Select, MultiSelect, Radio, CheckboxGroup, Tags}`; `IAuditable` fields emitted as `IsSystem=true, ReadOnly=true`; `ISeoMeta` types gain a `SEO` field group + fields `seoTitle`(Text)/`seoMetaDescription`(Textarea)/`seoOgImageId`(Number).

- [ ] **Step 1: Write the failing happy-path test**

```csharp
// tests/Struo.Tests/Metadata/MetadataScannerTests.cs
using AwesomeAssertions;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Metadata;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Metadata;

public class MetadataScannerTests
{
    private static CollectionMetadata ArticleMeta() =>
        MetadataScanner.ScanTypes([typeof(Article), typeof(Tag)])
            .Single(c => c.Name == "article");

    [Fact]
    public void Scans_collection_identity_in_camelCase()
    {
        var article = ArticleMeta();
        article.Name.Should().Be("article");
        article.Label.Should().Be("Article");
        article.Group.Should().Be("Content");
        article.DefaultDisplayField.Should().Be("title");
    }

    [Fact]
    public void Maps_field_interface_and_flags()
    {
        var title = ArticleMeta().Fields.Single(f => f.Name == "title");
        title.Interface.Should().Be(FieldInterface.Text);
        title.Required.Should().BeTrue();
        title.Searchable.Should().BeTrue();
        title.Translatable.Should().BeTrue();
        title.Group.Should().Be("Content");

        var body = ArticleMeta().Fields.Single(f => f.Name == "body");
        body.Interface.Should().Be(FieldInterface.RichText);
        body.Translatable.Should().BeTrue();
    }

    [Fact]
    public void Parses_cms_options_into_value_label_pairs()
    {
        var status = ArticleMeta().Fields.Single(f => f.Name == "status");
        status.Interface.Should().Be(FieldInterface.Select);
        status.Options.Should().NotBeNull();
        status.Options!.Should().ContainInOrder(
            new FieldOption("draft", "Draft"),
            new FieldOption("published", "Published"));
    }

    [Fact]
    public void Emits_audit_fields_as_system_readonly()
    {
        var createdAt = ArticleMeta().Fields.Single(f => f.Name == "createdAt");
        createdAt.IsSystem.Should().BeTrue();
        createdAt.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Applies_seo_field_group_for_iseometa()
    {
        var article = ArticleMeta();
        article.FieldGroups.Should().Contain(g => g.Name == "SEO");
        var seoTitle = article.Fields.Single(f => f.Name == "seoTitle");
        seoTitle.Interface.Should().Be(FieldInterface.Text);
        seoTitle.Group.Should().Be("SEO");
        article.Fields.Should().Contain(f => f.Name == "seoMetaDescription" && f.Interface == FieldInterface.Textarea);
        article.Fields.Should().Contain(f => f.Name == "seoOgImageId" && f.Interface == FieldInterface.Number);
    }

    [Fact]
    public void Scans_multiple_collections()
    {
        var all = MetadataScanner.ScanTypes([typeof(Article), typeof(Tag)]);
        all.Select(c => c.Name).Should().BeEquivalentTo(["article", "tag"]);
    }

    [Fact]
    public void Non_option_field_has_no_options()
    {
        ArticleMeta().Fields.Single(f => f.Name == "title").Options.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run the test — verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MetadataScannerTests`
Expected: FAIL — `MetadataScanner` does not exist.

- [ ] **Step 3: Implement the scanner**

```csharp
// src/Struo.Infrastructure/Metadata/MetadataScanner.cs
using System.Reflection;
using System.Text.Json;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Seo;

namespace Struo.Infrastructure.Metadata;

public static class MetadataScanner
{
    private static readonly HashSet<FieldInterface> OptionInterfaces =
    [
        FieldInterface.Select, FieldInterface.MultiSelect, FieldInterface.Radio,
        FieldInterface.CheckboxGroup, FieldInterface.Tags
    ];

    private static readonly string[] AuditFieldNames =
        [nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy),
         nameof(IAuditable.UpdatedAt), nameof(IAuditable.UpdatedBy)];

    public static IReadOnlyList<CollectionMetadata> Scan(params Assembly[] assemblies) =>
        ScanTypes(assemblies.SelectMany(a => a.GetTypes()));

    public static IReadOnlyList<CollectionMetadata> ScanTypes(IEnumerable<Type> types)
    {
        var collections = new List<CollectionMetadata>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var type in types)
        {
            var collectionAttr = type.GetCustomAttribute<CmsCollectionAttribute>();
            if (collectionAttr is null) continue;

            var meta = BuildCollection(type, collectionAttr);
            if (!seen.Add(meta.Name))
                throw new MetadataException(
                    $"Duplicate collection name '{meta.Name}' (type '{type.FullName}').");
            collections.Add(meta);
        }

        return collections;
    }

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    private static CollectionMetadata BuildCollection(Type type, CmsCollectionAttribute attr)
    {
        var groups = type.GetCustomAttributes<CmsFieldGroupAttribute>()
            .Select(g => new FieldGroupMetadata(g.Name, g.Label, g.Sort))
            .ToList();

        var fields = new List<(int order, FieldMetadata field)>();
        var order = 0;
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            order++;
            var fieldAttr = prop.GetCustomAttribute<CmsFieldAttribute>();
            var isAudit = AuditFieldNames.Contains(prop.Name);

            if (fieldAttr is not null)
                fields.Add((order, BuildField(prop, fieldAttr)));
            else if (isAudit)
                fields.Add((order, BuildSystemField(prop)));
            // properties with neither [CmsField] nor audit/SEO convention are ignored
        }

        // ISeoMeta convention
        if (typeof(ISeoMeta).IsAssignableFrom(type))
        {
            foreach (var seo in BuildSeoFields())
                fields.Add((++order, seo));
            if (groups.All(g => g.Name != "SEO"))
                groups.Add(new FieldGroupMetadata("SEO", "SEO", groups.Count + 1));
        }

        var ordered = fields
            .OrderBy(f => f.field.Sort)
            .ThenBy(f => f.order)
            .Select(f => f.field)
            .ToList();

        var name = Camel(type.Name);
        var defaultDisplay = attr.DefaultDisplayField is null ? null : Camel(attr.DefaultDisplayField);
        if (defaultDisplay is not null && ordered.All(f => f.Name != defaultDisplay))
            throw new MetadataException(
                $"Collection '{name}' DefaultDisplayField '{attr.DefaultDisplayField}' is not a known field.");

        return new CollectionMetadata
        {
            Name = name,
            Label = attr.Label,
            Icon = attr.Icon,
            Group = attr.Group,
            DefaultDisplayField = defaultDisplay,
            FieldGroups = groups,
            Fields = ordered
        };
    }

    private static FieldMetadata BuildField(PropertyInfo prop, CmsFieldAttribute attr)
    {
        IReadOnlyList<FieldOption>? options = null;
        var optionsAttr = prop.GetCustomAttribute<CmsOptionsAttribute>();
        if (optionsAttr is not null)
        {
            if (!OptionInterfaces.Contains(attr.Interface))
                throw new MetadataException(
                    $"Field '{prop.DeclaringType?.Name}.{prop.Name}' has [CmsOptions] " +
                    $"but interface '{attr.Interface}' is not an option type.");
            options = ParseOptions(prop, optionsAttr);
        }

        return new FieldMetadata
        {
            Name = Camel(prop.Name),
            Label = attr.Label ?? prop.Name,
            Interface = attr.Interface,
            Required = attr.Required,
            Searchable = attr.Searchable,
            Sortable = attr.Sortable,
            ReadOnly = attr.ReadOnly,
            Hidden = attr.Hidden,
            Translatable = attr.Translatable,
            Sort = attr.Sort,
            HelpText = attr.HelpText,
            Group = attr.Group,
            Options = options,
            IsSystem = false
        };
    }

    private static IReadOnlyList<FieldOption> ParseOptions(PropertyInfo prop, CmsOptionsAttribute attr)
    {
        var list = new List<FieldOption>();
        foreach (var entry in attr.Options)
        {
            var idx = entry.IndexOf(':');
            if (idx <= 0 || idx == entry.Length - 1)
                throw new MetadataException(
                    $"Field '{prop.DeclaringType?.Name}.{prop.Name}' has malformed [CmsOptions] entry '{entry}' (expected 'value:label').");
            list.Add(new FieldOption(entry[..idx], entry[(idx + 1)..]));
        }
        return list;
    }

    private static FieldMetadata BuildSystemField(PropertyInfo prop) => new()
    {
        Name = Camel(prop.Name),
        Label = prop.Name,
        Interface = prop.PropertyType == typeof(DateTime) || prop.PropertyType == typeof(DateTime?)
            ? FieldInterface.DateTime
            : FieldInterface.Text,
        ReadOnly = true,
        IsSystem = true,
        Hidden = false,
        Sort = 1000
    };

    private static IEnumerable<FieldMetadata> BuildSeoFields()
    {
        yield return new FieldMetadata
        {
            Name = "seoTitle", Label = "SEO Title", Interface = FieldInterface.Text,
            Group = "SEO", Sort = 900
        };
        yield return new FieldMetadata
        {
            Name = "seoMetaDescription", Label = "SEO Meta Description", Interface = FieldInterface.Textarea,
            Group = "SEO", Sort = 901
        };
        yield return new FieldMetadata
        {
            Name = "seoOgImageId", Label = "OG Image", Interface = FieldInterface.Number,
            Group = "SEO", Sort = 902
        };
    }
}
```

> The scanner uses only BCL reflection + System.Text.Json. `Type.GetProperties()` order is unspecified, so ordering uses `Sort` then a captured declaration index (`order`).

- [ ] **Step 4: Run the tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~MetadataScannerTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS (all).

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Infrastructure/Metadata/MetadataScanner.cs tests/Struo.Tests/Metadata/MetadataScannerTests.cs
git commit -m "feat: add MetadataScanner with field/option/audit/SEO scanning"
```

---

### Task 4: Scanner validation (TDD)

**Files:**
- Modify: `src/Struo.Infrastructure/Metadata/MetadataScanner.cs` (validation already implemented in Task 3; this task proves it and adds fixtures)
- Test: `tests/Struo.Tests/Metadata/MetadataValidationTests.cs`

**Interfaces:**
- Consumes: `MetadataScanner.ScanTypes`, `MetadataException`.
- Produces: test-only fixture entities proving the three validation rules.

- [ ] **Step 1: Write the failing validation tests (with fixtures)**

```csharp
// tests/Struo.Tests/Metadata/MetadataValidationTests.cs
using AwesomeAssertions;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

public class MetadataValidationTests
{
    [CmsCollection("Dup")]
    private sealed class DupA
    {
        [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [CmsCollection("Dup")]
    private sealed class DupB
    {
        [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [CmsCollection("BadDisplay", DefaultDisplayField = "Missing")]
    private sealed class BadDisplay
    {
        [CmsField(Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [CmsCollection("BadOptions")]
    private sealed class BadOptions
    {
        [CmsField(Interface = FieldInterface.Text)]
        [CmsOptions("a:A")]
        public string Name { get; set; } = "";
    }

    [Fact]
    public void Throws_on_duplicate_collection_name()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(DupA), typeof(DupB)]);
        act.Should().Throw<MetadataException>().WithMessage("*Duplicate collection name*");
    }

    [Fact]
    public void Throws_when_default_display_field_unknown()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(BadDisplay)]);
        act.Should().Throw<MetadataException>().WithMessage("*DefaultDisplayField*");
    }

    [Fact]
    public void Throws_when_cms_options_on_non_option_interface()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(BadOptions)]);
        act.Should().Throw<MetadataException>().WithMessage("*not an option type*");
    }
}
```

> `DefaultDisplayField = "Missing"` camelCases to `missing`, matching no field → throws. The fixtures are `private` nested classes, so no assembly-wide scan picks them up.

- [ ] **Step 2: Run the tests — verify they pass (validation already implemented in Task 3)**

Run: `dotnet test --filter FullyQualifiedName~MetadataValidationTests`
Expected: PASS (3 tests). If any fail, align the corresponding `throw new MetadataException(...)` message substring in `MetadataScanner` — do not weaken the validation.

- [ ] **Step 3: Commit**

```bash
git add tests/Struo.Tests/Metadata/MetadataValidationTests.cs src/Struo.Infrastructure/Metadata/MetadataScanner.cs
git commit -m "test: cover metadata scanner validation rules"
```

---

### Task 5: IMetadataProvider + CachedMetadataProvider + AddStruoMetadata (TDD)

**Files:**
- Create: `src/Struo.Application/Metadata/IMetadataProvider.cs`
- Create: `src/Struo.Infrastructure/Metadata/CachedMetadataProvider.cs`
- Create: `src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs`
- Test: `tests/Struo.Tests/Metadata/CachedMetadataProviderTests.cs`

**Interfaces:**
- Consumes: `CollectionMetadata`, `MetadataScanner.Scan`.
- Produces:
  - `interface IMetadataProvider { IReadOnlyList<CollectionMetadata> GetCollections(); CollectionMetadata? GetCollection(string name); }`
  - `sealed class CachedMetadataProvider(IReadOnlyList<CollectionMetadata>) : IMetadataProvider` (case-insensitive lookup)
  - `static IServiceCollection AddStruoMetadata(this IServiceCollection, params Assembly[])` — scans eagerly, registers a singleton `IMetadataProvider`.

- [ ] **Step 1: Create the port**

```csharp
// src/Struo.Application/Metadata/IMetadataProvider.cs
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

public interface IMetadataProvider
{
    IReadOnlyList<CollectionMetadata> GetCollections();
    CollectionMetadata? GetCollection(string name);
}
```

- [ ] **Step 2: Write the failing test**

```csharp
// tests/Struo.Tests/Metadata/CachedMetadataProviderTests.cs
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Metadata;
using Struo.Infrastructure.DependencyInjection;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Metadata;

public class CachedMetadataProviderTests
{
    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddStruoMetadata(typeof(Article).Assembly);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_exposes_scanned_collections()
    {
        using var sp = Build();
        var provider = sp.GetRequiredService<IMetadataProvider>();
        provider.GetCollections().Select(c => c.Name)
            .Should().Contain(["article", "tag"]);
    }

    [Fact]
    public void GetCollection_is_case_insensitive_and_null_safe()
    {
        using var sp = Build();
        var provider = sp.GetRequiredService<IMetadataProvider>();
        provider.GetCollection("ARTICLE").Should().NotBeNull();
        provider.GetCollection("nope").Should().BeNull();
    }

    [Fact]
    public void Registry_is_cached_singleton_returning_same_instances()
    {
        using var sp = Build();
        var a = sp.GetRequiredService<IMetadataProvider>();
        var b = sp.GetRequiredService<IMetadataProvider>();
        a.Should().BeSameAs(b);                                   // singleton, scanned once
        a.GetCollection("article").Should().BeSameAs(b.GetCollection("article")); // cached instance
    }
}
```

- [ ] **Step 3: Run the test — verify it fails**

Run: `dotnet test --filter FullyQualifiedName~CachedMetadataProviderTests`
Expected: FAIL — `AddStruoMetadata`/`CachedMetadataProvider` do not exist.

- [ ] **Step 4: Implement the provider and DI extension**

```csharp
// src/Struo.Infrastructure/Metadata/CachedMetadataProvider.cs
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Models;

namespace Struo.Infrastructure.Metadata;

public sealed class CachedMetadataProvider : IMetadataProvider
{
    private readonly IReadOnlyList<CollectionMetadata> _all;
    private readonly IReadOnlyDictionary<string, CollectionMetadata> _byName;

    public CachedMetadataProvider(IReadOnlyList<CollectionMetadata> collections)
    {
        _all = collections;
        _byName = collections.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<CollectionMetadata> GetCollections() => _all;

    public CollectionMetadata? GetCollection(string name) =>
        _byName.TryGetValue(name, out var c) ? c : null;
}
```

```csharp
// src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Metadata;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.DependencyInjection;

public static class MetadataServiceCollectionExtensions
{
    public static IServiceCollection AddStruoMetadata(
        this IServiceCollection services, params Assembly[] assemblies)
    {
        // Eager scan at registration -> immutable singleton. No per-request reflection.
        var collections = MetadataScanner.Scan(assemblies);
        services.AddSingleton<IMetadataProvider>(new CachedMetadataProvider(collections));
        return services;
    }
}
```

- [ ] **Step 5: Run the tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~CachedMetadataProviderTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Struo.Application/Metadata src/Struo.Infrastructure/Metadata/CachedMetadataProvider.cs src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs tests/Struo.Tests/Metadata/CachedMetadataProviderTests.cs
git commit -m "feat: add cached metadata provider and AddStruoMetadata registration"
```

---

### Task 6: SchemaService + SchemaController + wiring + integration (TDD)

**Files:**
- Create: `src/Struo.Application/Metadata/SchemaService.cs`
- Create: `src/Struo.Api/Controllers/SchemaController.cs`
- Modify: `src/Struo.Api/Program.cs` (register metadata + SchemaService; add enum→camelCase-string JSON converter; add `Tag` to dev InitTables)
- Test: `tests/Struo.Tests/Api/SchemaEndpointTests.cs`

**Interfaces:**
- Consumes: `IMetadataProvider`, `AddStruoMetadata`, `CollectionMetadata`, `Article`, `Tag`.
- Produces:
  - `sealed class SchemaService(IMetadataProvider) { IReadOnlyList<CollectionMetadata> GetAll(); CollectionMetadata? Get(string collection); }`
  - `GET /api/schema`, `GET /api/schema/{collection}` (404 unknown).

- [ ] **Step 1: Create `SchemaService`**

```csharp
// src/Struo.Application/Metadata/SchemaService.cs
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

// Thin use-case seam; Phase 6 adds role-based field filtering here.
public sealed class SchemaService(IMetadataProvider provider)
{
    public IReadOnlyList<CollectionMetadata> GetAll() => provider.GetCollections();
    public CollectionMetadata? Get(string collection) => provider.GetCollection(collection);
}
```

- [ ] **Step 2: Write the failing integration tests**

```csharp
// tests/Struo.Tests/Api/SchemaEndpointTests.cs
using System.Net;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

public class SchemaEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Schema_lists_all_collections()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/schema");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"name\":\"article\"");
        body.Should().Contain("\"name\":\"tag\"");
    }

    [Fact]
    public async Task Schema_for_collection_includes_interfaces_options_and_seo()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/schema/article");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"interface\":\"richText\"");   // enum as camelCase string
        body.Should().Contain("\"translatable\":true");
        body.Should().Contain("\"value\":\"draft\"");          // option pair
        body.Should().Contain("\"name\":\"seoTitle\"");        // SEO convention applied
        body.Should().Contain("\"isSystem\":true");            // audit fields
    }

    [Fact]
    public async Task Schema_for_unknown_collection_returns_404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/schema/does-not-exist");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

- [ ] **Step 3: Run the tests — verify they fail**

Run: `dotnet test --filter FullyQualifiedName~SchemaEndpointTests`
Expected: FAIL — no `/api/schema` route (404 for all), enum-string assertion unmet.

- [ ] **Step 4: Create `SchemaController`**

```csharp
// src/Struo.Api/Controllers/SchemaController.cs
using Microsoft.AspNetCore.Mvc;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Models;

namespace Struo.Api.Controllers;

[ApiController]
[Route("api/schema")]
public sealed class SchemaController(SchemaService schema) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<CollectionMetadata>> GetAll() => Ok(schema.GetAll());

    [HttpGet("{collection}")]
    public ActionResult<CollectionMetadata> Get(string collection)
    {
        var meta = schema.Get(collection);
        return meta is null ? NotFound() : Ok(meta);
    }
}
```

- [ ] **Step 5: Wire Program.cs**

In `src/Struo.Api/Program.cs`: (a) add the enum→camelCase-string converter to the JSON options; (b) register metadata + `SchemaService`; (c) add `Tag` to the dev InitTables call.

Replace the controllers/JSON + service-registration block with:

```csharp
    builder.Services
        .AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        });

    builder.Services.AddOpenApi();
    builder.Services.AddStruoInfrastructure(builder.Configuration);
    builder.Services.AddStruoMetadata(typeof(Article).Assembly);
    builder.Services.AddScoped<SchemaService>();
    builder.Services.AddHealthChecks()
        .AddCheck<DbReadinessCheck>("database", tags: ["ready"]);
```

Add these usings at the top of `Program.cs` (alongside the existing ones): `using Struo.Application.Metadata;` (for `SchemaService`). `AddStruoMetadata` lives in `Struo.Infrastructure.DependencyInjection`, which is already imported (it is the namespace of `AddStruoInfrastructure`); `Article`/`Tag` are in `Struo.Sample.Blog`, already imported.

Update the dev InitTables block to include `Tag`:

```csharp
    if (app.Environment.IsDevelopment())
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment, typeof(Article), typeof(Tag));
    }
```

- [ ] **Step 6: Run the tests — verify they pass**

Run: `dotnet test --filter FullyQualifiedName~SchemaEndpointTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Run the full suite**

Run: `dotnet test`
Expected: PASS (all — Phase 0 + Phase 1).

- [ ] **Step 8: Commit**

```bash
git add src/Struo.Application/Metadata/SchemaService.cs src/Struo.Api/Controllers/SchemaController.cs src/Struo.Api/Program.cs tests/Struo.Tests/Api/SchemaEndpointTests.cs
git commit -m "feat: expose GET /api/schema backed by cached metadata registry"
```

---

## Self-Review

**1. Spec coverage** (design doc → tasks):

| Spec item | Task |
|---|---|
| §4 attribute set (incl. CmsRelation/CmsTranslations defined) | Task 1 |
| §6 FieldInterface + RelationInterface catalogs | Task 1 |
| Field groups (CmsFieldGroup) | Task 1, 3 |
| Metadata models (Domain) | Task 1 |
| ISeoMeta + SEO field-group convention (§9) | Task 1, 3 |
| Audit fields as system/readonly (§5) | Task 3 |
| Scanner + validation + naming (camelCase) | Task 3, 4 |
| IMetadataProvider + cached registry + AddStruoMetadata (no per-request reflection) | Task 5 |
| GET /api/schema + /{collection} + 404 | Task 6 |
| Enum as camelCase string in JSON | Task 6 |
| Multi-collection (Article + Tag) | Task 2, 3, 5, 6 |
| Caching proven | Task 5 |

No gaps.

**2. Placeholder scan:** No "TBD"/"implement later"; every code step has full code. Validation is implemented in Task 3 and proven in Task 4 (called out so the Task 4 implementer doesn't expect new production code unless a message substring needs aligning).

**3. Type consistency:** `MetadataScanner.Scan`/`ScanTypes`, `CollectionMetadata`/`FieldMetadata`/`FieldOption`/`FieldGroupMetadata` (exact init properties), `IMetadataProvider.GetCollections()/GetCollection(string)`, `CachedMetadataProvider(IReadOnlyList<CollectionMetadata>)`, `AddStruoMetadata(params Assembly[])`, `SchemaService.GetAll()/Get(string)` are used consistently. camelCase names (`article`, `title`, `seoTitle`) are consistent between scanner output and endpoint assertions.

**Known verify-against-runtime point:** `Type.GetProperties()` ordering is unspecified; the scanner sorts by `Sort` then a captured declaration index, so output ordering is deterministic.
