# StruoCMS Phase 3a (Relations Foundation) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make relations first-class — scan `[Navigate]`+`[CmsRelation]` into relation metadata, build/validate a relationship graph at startup, expose relations in the schema API, and support `deep` read expansion, M2M assignment writes, and OnDelete `Restrict` enforcement across the full Blog relation set.

**Architecture:** Relation metadata records in Domain; `IRelationshipGraph` port in Application; a relation scanner + graph + a "stitching" deep-expander (batched follow-up queries reusing the Phase-2 by-type `Queryable<T>`+`In`) + junction sync + Restrict checks in Infrastructure; `deep` transport in Api. **Deep expansion uses batched follow-up queries, NOT SqlSugar `.Includes()`** — portable, N+1-safe, avoids runtime expression-tree building.

**Tech Stack:** .NET 10, ASP.NET Core Controllers, SqlSugarCore (`[Navigate]`, `Queryable<T>().In(...)`), System.Text.Json, xUnit + AwesomeAssertions, SQLite (tests) / PostgreSQL (runtime).

## Global Constraints

- **Dependency rule (§2):** Domain → nothing; Application → Domain; Infrastructure → Application+Domain; Api → Application+Infrastructure; samples → Domain+SqlSugar; only Api references samples.
- **Domain purity (§2):** Domain has zero external packages. `RelationMetadata`/`RelationKind`/`DeepSpec` are plain C#. `[Navigate]` (SqlSugar) lives only on sample entities.
- **Zero vendor SQL (§15):** all DB access via SqlSugar ORM (`Queryable<T>`, `In`, junction CRUD); no raw SQL. Runs on SQLite + PostgreSQL.
- **FK rule (§8):** FK columns live only on the M2O side; O2M/M2M nav collections are `[SugarColumn(IsIgnore=true)]` views.
- **No per-request attribute reflection (§3):** relation metadata + graph built once at startup (in `AddStruoMetadata`).
- **camelCase JSON (§1):** relation names, fields, enum values camelCase.
- **No auto-load (§8):** list/get never expand relations unless `deep` is supplied.
- **Deferred to 3b:** cross-relation filter/sort (dotted filter/sort paths stay 400).
- **Packages (§15):** `dotnet add package` latest, CPM. **Build `TreatWarningsAsErrors=true` → 0 warnings. TDD.** Tests use `using AwesomeAssertions;`.

---

## File Structure

| File | Responsibility |
|---|---|
| `samples/Struo.Sample.Blog/Author.cs` | M2O target |
| `samples/Struo.Sample.Blog/Category.cs` | self-ref tree + O2M reverse |
| `samples/Struo.Sample.Blog/ArticleTag.cs` | M2M junction (not a collection) |
| `samples/Struo.Sample.Blog/Article.cs` (modify) | + Author/Category M2O, Tags M2M |
| `samples/Struo.Sample.Blog/Tag.cs` (modify) | + Articles reverse M2M |
| `src/Struo.Domain/Metadata/Enums/RelationKind.cs` | M2O/O2M/M2M |
| `src/Struo.Domain/Metadata/Models/RelationMetadata.cs` | relation metadata record |
| `src/Struo.Domain/Metadata/Models/CollectionMetadata.cs` (modify) | + `Relations` |
| `src/Struo.Domain/Query/DeepSpec.cs` | `deep` request model |
| `src/Struo.Domain/Query/QueryModel.cs` (modify) | + `Deep` init property |
| `src/Struo.Domain/Query/RelationConflictException.cs` | Restrict conflict → 409 |
| `src/Struo.Application/Metadata/IRelationshipGraph.cs` | graph port |
| `src/Struo.Application/Configuration/StruoQueryOptions.cs` (modify) | + `MaxRelationDepth` |
| `src/Struo.Infrastructure/Metadata/MetadataScanner.cs` (modify) | scan relations + junction descriptors |
| `src/Struo.Infrastructure/Metadata/RelationshipGraph.cs` | graph impl + validation + inbound-Restrict + junction descriptors |
| `src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs` (modify) | register graph |
| `src/Struo.Infrastructure/Query/RelationExpander.cs` | batched deep expansion |
| `src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs` (modify) | where-IN helper, junction sync, by-type query |
| `src/Struo.Infrastructure/DependencyInjection/DataServiceCollectionExtensions.cs` (modify) | register RelationExpander |
| `src/Struo.Application/Query/ItemService.cs` (modify) | deep validate+nest, M2M write, delete Restrict |
| `src/Struo.Application/Query/QueryParser.cs` (modify) | parse `deep` |
| `src/Struo.Application/Query/IItemRepository.cs` (modify) | + where-IN / junction-sync methods |
| `src/Struo.Api/Controllers/ItemsController.cs` (modify) | pass `deep` (incl. GET /{id}) |
| `src/Struo.Api/Program.cs` (modify) | dev InitTables list; 409 middleware |
| `tests/Struo.Tests/Query/RelationScannerTests.cs` | scanner/graph |
| `tests/Struo.Tests/Api/RelationSchemaTests.cs` | schema relations |
| `tests/Struo.Tests/Query/DeepExpansionTests.cs` | deep stitching |
| `tests/Struo.Tests/Query/RelationWriteTests.cs` | M2M + Restrict |

---

### Task 1: Sample relation entities

**Files:** Create `Author.cs`, `Category.cs`, `ArticleTag.cs`; modify `Article.cs`, `Tag.cs`, `src/Struo.Api/Program.cs` (dev InitTables list).

**Interfaces:**
- Consumes: Phase 1/2 attributes (`CmsCollection`, `CmsField`, `CmsRelation`, `RelationInterface`, `OnDelete`), `IAuditable`, SqlSugar `Navigate`/`SugarColumn`.
- Produces: entities `Author`, `Category`, `ArticleTag`; `Article` with `Author`/`Category` (M2O) + `Tags` (M2M); `Category` with `Parent`/`Children`/`Articles`; `Tag` with `Articles`.

- [ ] **Step 1: Create `Author.cs`**

```csharp
// samples/Struo.Sample.Blog/Author.cs
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("authors")]
[CmsCollection("Author", Icon = "person", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Author : IAuditable
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

- [ ] **Step 2: Create `Category.cs`** (self-referential tree + reverse articles)

```csharp
// samples/Struo.Sample.Blog/Category.cs
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

[SugarTable("categories")]
[CmsCollection("Category", Icon = "folder", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Category : IAuditable
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    public long? ParentId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.TreeSelect, DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public Category? Parent { get; set; }

    [Navigate(NavigateType.OneToMany, nameof(ParentId))]
    [CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<Category> Children { get; set; } = [];

    [Navigate(NavigateType.OneToMany, nameof(Article.CategoryId))]
    [CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Title}")]
    [SugarColumn(IsIgnore = true)]
    public List<Article> Articles { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? CreatedBy { get; set; }
    public DateTime UpdatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public string? UpdatedBy { get; set; }
}
```

- [ ] **Step 3: Create `ArticleTag.cs`** (junction; NOT a `[CmsCollection]`)

```csharp
// samples/Struo.Sample.Blog/ArticleTag.cs
using SqlSugar;

namespace Struo.Sample.Blog;

[SugarTable("article_tags")]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }
    public long ArticleId { get; set; }
    public long TagId { get; set; }
    public int SortOrder { get; set; }
}
```

- [ ] **Step 4: Modify `Article.cs`** — add these members (keep all existing fields, audit, SEO; `using SqlSugar;` already present):

```csharp
    // --- relations (Phase 3a) ---
    public long AuthorId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(AuthorId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}", OnDelete = OnDelete.Restrict)]
    [SugarColumn(IsIgnore = true)]
    public Author? Author { get; set; }

    [SugarColumn(IsNullable = true)]
    public long? CategoryId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(CategoryId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public Category? Category { get; set; }

    [Navigate(typeof(ArticleTag), nameof(ArticleTag.ArticleId), nameof(ArticleTag.TagId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}", SortField = nameof(ArticleTag.SortOrder))]
    [SugarColumn(IsIgnore = true)]
    public List<Tag> Tags { get; set; } = [];
```

`AuthorId` is non-nullable; `CategoryId` nullable.

- [ ] **Step 5: Modify `Tag.cs`** — add reverse M2M (keep existing):

```csharp
    [Navigate(typeof(ArticleTag), nameof(ArticleTag.TagId), nameof(ArticleTag.ArticleId))]
    [CmsRelation(Interface = RelationInterface.RelatedList, DisplayTemplate = "{Title}")]
    [SugarColumn(IsIgnore = true)]
    public List<Article> Articles { get; set; } = [];
```

(Ensure `using SqlSugar;`, `using Struo.Domain.Metadata.Enums;` present in `Tag.cs`.)

- [ ] **Step 6: Update dev InitTables in `Program.cs`**

```csharp
        DatabaseInitializer.InitializeDevelopmentSchema(db, app.Environment,
            typeof(Article), typeof(Tag), typeof(Author), typeof(Category), typeof(ArticleTag));
```

- [ ] **Step 7: Build + run existing suite**

Run: `dotnet build` → 0 warnings. Run: `dotnet test` → all green. (Nav collections are `IsIgnore`, so SqlSugar CodeFirst won't create columns for them; per-test SQLite DBs are fresh so the new `AuthorId` column is fine. Existing repo/projection tests insert `Article` without `AuthorId` → SqlSugar inserts default `0`; if any test asserts on field sets, it is unaffected since `authorId`/`categoryId`/`tags` are not `[CmsField]` so they are not projected.)

> If `Article` now requiring an author breaks an existing create test, the test data simply omits it (FK 0) — do NOT add `[CmsField]` to FK columns and do NOT make AuthorId required in metadata this task.

- [ ] **Step 8: Commit**

```bash
git add samples/Struo.Sample.Blog src/Struo.Api/Program.cs
git commit -m "feat: add Blog relation entities (Author, Category tree, Article-Tag M2M)"
```

---

### Task 2: Domain relation metadata

**Files:** Create `RelationKind.cs`, `RelationMetadata.cs`; modify `CollectionMetadata.cs`.

**Interfaces:**
- Consumes: `RelationInterface`, `OnDelete`.
- Produces: `enum RelationKind { ManyToOne, OneToMany, ManyToMany }`; `RelationMetadata` record; `CollectionMetadata.Relations` (default empty).

- [ ] **Step 1: Create enum + record**

```csharp
// src/Struo.Domain/Metadata/Enums/RelationKind.cs
namespace Struo.Domain.Metadata.Enums;

public enum RelationKind { ManyToOne, OneToMany, ManyToMany }
```

```csharp
// src/Struo.Domain/Metadata/Models/RelationMetadata.cs
using Struo.Domain.Metadata.Enums;

namespace Struo.Domain.Metadata.Models;

public sealed record RelationMetadata
{
    public required string Name { get; init; }
    public required string Label { get; init; }
    public required RelationKind Kind { get; init; }
    public required string TargetCollection { get; init; }
    public required RelationInterface Interface { get; init; }
    public string? ForeignKey { get; init; }
    public string? DisplayTemplate { get; init; }
    public string? PickerQuery { get; init; }
    public OnDelete OnDelete { get; init; }
    public bool Editable { get; init; }
    public bool SelfReferencing { get; init; }
}
```

- [ ] **Step 2: Add `Relations` to `CollectionMetadata`** (keep existing properties):

```csharp
    public IReadOnlyList<RelationMetadata> Relations { get; init; } = [];
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Struo.Domain` → 0 warnings; Domain still has no `<PackageReference>`.

- [ ] **Step 4: Commit**

```bash
git add src/Struo.Domain/Metadata
git commit -m "feat: add RelationKind and RelationMetadata to Domain"
```

---

### Task 3: Relation scanner + relationship graph + validation

**Files:** Modify `MetadataScanner.cs`; create `IRelationshipGraph.cs`, `RelationshipGraph.cs`; modify `MetadataServiceCollectionExtensions.cs`.
**Test:** `tests/Struo.Tests/Query/RelationScannerTests.cs`.

**Interfaces:**
- Consumes: `[Navigate]`/`[CmsRelation]`, `RelationMetadata`.
- Produces:
  - `MetadataScanner.ScanRelations(Type) → IReadOnlyList<RelationMetadata>`; `Scan` populates `CollectionMetadata.Relations`.
  - `MetadataScanner.ScanRelationDescriptors(IEnumerable<Type>) → IReadOnlyDictionary<string, IReadOnlyList<RelationDescriptor>>` where `RelationDescriptor` (Infrastructure-internal) carries the serialized `RelationMetadata` PLUS junction `Type`, junction parent/target FK property names, O2M reverse-FK property — for the expander/junction-sync.
  - `IRelationshipGraph { IReadOnlyList<RelationMetadata> Relations(string); RelationMetadata? Resolve(string, string); IReadOnlyList<RelationDescriptor> Descriptors(string); IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundRestrict(string); }` (RelationDescriptor lives in Infrastructure; expose it via the concrete `RelationshipGraph`, and keep `IRelationshipGraph` to the serializable surface + InboundRestrict — see note).

> To respect the dependency rule, `IRelationshipGraph` (Application) returns only Domain types (`RelationMetadata`) + `InboundRestrict` tuples. The richer `RelationDescriptor` (junction Type etc.) is an Infrastructure concept; the `RelationExpander` and repository (both Infrastructure) take the concrete `RelationshipGraph` (or a dedicated Infrastructure interface) to read descriptors. Register both the interface and the concrete type as the same singleton instance.

- [ ] **Step 1: Create the Application port**

```csharp
// src/Struo.Application/Metadata/IRelationshipGraph.cs
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

public interface IRelationshipGraph
{
    IReadOnlyList<RelationMetadata> Relations(string collection);
    RelationMetadata? Resolve(string collection, string relationName);
    IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundRestrict(string targetCollection);
}
```

- [ ] **Step 2: Write the failing scanner tests**

```csharp
// tests/Struo.Tests/Query/RelationScannerTests.cs
using AwesomeAssertions;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Query;

public class RelationScannerTests
{
    private static IReadOnlyList<Struo.Domain.Metadata.Models.RelationMetadata> Rel(Type t) =>
        MetadataScanner.ScanRelations(t);

    [Fact]
    public void Scans_m2o_author_with_foreign_key()
    {
        var author = Rel(typeof(Article)).Single(r => r.Name == "author");
        author.Kind.Should().Be(RelationKind.ManyToOne);
        author.TargetCollection.Should().Be("author");
        author.ForeignKey.Should().Be("authorId");
        author.Interface.Should().Be(RelationInterface.Dropdown);
        author.OnDelete.Should().Be(OnDelete.Restrict);
        author.SelfReferencing.Should().BeFalse();
    }

    [Fact]
    public void Scans_m2m_tags()
    {
        var tags = Rel(typeof(Article)).Single(r => r.Name == "tags");
        tags.Kind.Should().Be(RelationKind.ManyToMany);
        tags.TargetCollection.Should().Be("tag");
        tags.Interface.Should().Be(RelationInterface.TagSelect);
    }

    [Fact]
    public void Scans_self_referential_category_tree()
    {
        var rels = Rel(typeof(Category));
        var parent = rels.Single(r => r.Name == "parent");
        parent.Kind.Should().Be(RelationKind.ManyToOne);
        parent.SelfReferencing.Should().BeTrue();
        rels.Single(r => r.Name == "children").Kind.Should().Be(RelationKind.OneToMany);
        rels.Should().Contain(r => r.Name == "articles" && r.Kind == RelationKind.OneToMany);
    }
}
```

- [ ] **Step 3: Run — verify fail**

Run: `dotnet test --filter FullyQualifiedName~RelationScannerTests`
Expected: FAIL — `ScanRelations` missing.

- [ ] **Step 4: Implement `ScanRelations`** in `MetadataScanner` (add `using System.Collections;`, `using SqlSugar;`)

```csharp
public static IReadOnlyList<RelationMetadata> ScanRelations(Type type)
{
    var list = new List<RelationMetadata>();
    foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
        var nav = prop.GetCustomAttribute<NavigateAttribute>();
        var rel = prop.GetCustomAttribute<CmsRelationAttribute>();
        if (nav is null || rel is null) continue;

        var isCollection = prop.PropertyType.IsGenericType
            && typeof(IEnumerable).IsAssignableFrom(prop.PropertyType);
        Type target;
        RelationKind kind;
        string? fk = null;

        if (isCollection)
        {
            target = prop.PropertyType.GetGenericArguments()[0];
            kind = NavigateHasMappingType(nav) ? RelationKind.ManyToMany : RelationKind.OneToMany;
        }
        else
        {
            target = prop.PropertyType;
            kind = RelationKind.ManyToOne;
            fk = Camel(NavigateForeignKeyName(nav));
        }

        list.Add(new RelationMetadata
        {
            Name = Camel(prop.Name),
            Label = prop.Name,
            Kind = kind,
            TargetCollection = Camel(target.Name),
            Interface = rel.Interface,
            ForeignKey = fk,
            DisplayTemplate = rel.DisplayTemplate,
            PickerQuery = rel.PickerQuery,
            OnDelete = rel.OnDelete,
            Editable = rel.Editable,
            SelfReferencing = target == type
        });
    }
    return list;
}
```

Implement the two SqlSugar helpers — **verify the exact `NavigateAttribute` members against installed SqlSugarCore 5.1.4.215; if not publicly exposed, read constructor args via `CustomAttributeData`:**

```csharp
private static bool NavigateHasMappingType(NavigateAttribute nav) =>
    nav.MappingType is not null;   // M2M was constructed with a junction Type

private static string NavigateForeignKeyName(NavigateAttribute nav) =>
    nav.Name;                      // FK property name for OneToOne/M2O navigates
```

> If `NavigateAttribute.MappingType` / `.Name` are not the right member names in this version, locate the correct ones (e.g. `GetNavigateType`, `MappingA`/`MappingB`, or via `prop.CustomAttributes` constructor arguments) and adjust ONLY these two helpers. The tests pin the expected behavior.

- [ ] **Step 5: Populate `CollectionMetadata.Relations`** — in `BuildCollection`, add `Relations = ScanRelations(type)` to the returned object initializer.

- [ ] **Step 6: Create `RelationshipGraph` (+ internal `RelationDescriptor`) + validation; register**

```csharp
// src/Struo.Infrastructure/Metadata/RelationshipGraph.cs
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Infrastructure.Metadata;

// Infrastructure-only richer descriptor for expander/junction-sync.
public sealed record RelationDescriptor(
    RelationMetadata Meta,
    Type? JunctionType, string? JunctionParentFk, string? JunctionTargetFk, string? JunctionSort,
    string? ReverseForeignKeyProperty);

public sealed class RelationshipGraph : IRelationshipGraph
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<RelationDescriptor>> _byCollection;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<(string, string)>> _inboundRestrict;

    public RelationshipGraph(IReadOnlyList<CollectionMetadata> collections, IReadOnlyDictionary<string, Type> collectionTypes)
    {
        var known = collections.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var map = new Dictionary<string, IReadOnlyList<RelationDescriptor>>(StringComparer.OrdinalIgnoreCase);
        var inbound = new Dictionary<string, List<(string, string)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var c in collections)
        {
            var descriptors = new List<RelationDescriptor>();
            var entityType = collectionTypes[c.Name];
            foreach (var r in c.Relations)
            {
                if (!known.Contains(r.TargetCollection))
                    throw new MetadataException($"Relation '{c.Name}.{r.Name}' targets unknown collection '{r.TargetCollection}'.");
                if (r.Kind == RelationKind.ManyToOne && string.IsNullOrEmpty(r.ForeignKey))
                    throw new MetadataException($"M2O relation '{c.Name}.{r.Name}' has no foreign key.");

                descriptors.Add(BuildDescriptor(entityType, r));

                if (r.Kind == RelationKind.ManyToOne && r.OnDelete == OnDelete.Restrict)
                {
                    if (!inbound.TryGetValue(r.TargetCollection, out var lst))
                        inbound[r.TargetCollection] = lst = [];
                    lst.Add((c.Name, r.ForeignKey!));
                }
            }
            map[c.Name] = descriptors;
        }
        _byCollection = map;
        _inboundRestrict = inbound.ToDictionary(k => k.Key, v => (IReadOnlyList<(string, string)>)v.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static RelationDescriptor BuildDescriptor(Type entityType, RelationMetadata r)
    {
        var prop = entityType.GetProperties().First(p =>
            string.Equals(JsonNamingPolicyCamel(p.Name), r.Name, StringComparison.OrdinalIgnoreCase));
        var nav = prop.GetCustomAttribute<NavigateAttribute>()!;
        if (r.Kind == RelationKind.ManyToMany)
            return new RelationDescriptor(r, NavigateMappingType(nav), NavigateMappingA(nav), NavigateMappingB(nav), NavigateMappingSort(prop), null);
        if (r.Kind == RelationKind.OneToMany)
            return new RelationDescriptor(r, null, null, null, null, NavigateForeignKeyName(nav)); // reverse FK property on the child
        return new RelationDescriptor(r, null, null, null, null, null);
    }

    public IReadOnlyList<RelationMetadata> Relations(string collection) =>
        _byCollection.TryGetValue(collection, out var d) ? d.Select(x => x.Meta).ToList() : [];

    public RelationMetadata? Resolve(string collection, string relationName) =>
        Relations(collection).FirstOrDefault(r => string.Equals(r.Name, relationName, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<RelationDescriptor> Descriptors(string collection) =>
        _byCollection.TryGetValue(collection, out var d) ? d : [];

    public IReadOnlyList<(string SourceCollection, string ForeignKey)> InboundRestrict(string targetCollection) =>
        _inboundRestrict.TryGetValue(targetCollection, out var l) ? l : [];

    private static string JsonNamingPolicyCamel(string s) => System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(s);
    // Navigate accessor helpers (verify against SqlSugar; mirror MetadataScanner's helpers):
    private static Type? NavigateMappingType(NavigateAttribute n) => n.MappingType;
    private static string? NavigateMappingA(NavigateAttribute n) => n.MappingA; // junction parent FK property
    private static string? NavigateMappingB(NavigateAttribute n) => n.MappingB; // junction target FK property
    private static string NavigateForeignKeyName(NavigateAttribute n) => n.Name;
    private static string? NavigateMappingSort(PropertyInfo prop) =>
        prop.GetCustomAttribute<CmsRelationAttribute>()?.SortField;
}
```

> **Verify the `NavigateAttribute` accessors** (`MappingType`, `MappingA`, `MappingB`, `Name`) against installed SqlSugar; adjust the private helpers (and the matching ones in `MetadataScanner`) to the real member names. The `SortField` from `[CmsRelation]` is the junction sort property (e.g. `SortOrder`).

In `AddStruoMetadata`, build a `collectionTypes` dict (camel name → entity Type, from the scanned types) and register the graph as BOTH `IRelationshipGraph` and the concrete `RelationshipGraph` (same instance):

```csharp
        var graph = new RelationshipGraph(collections, collectionTypes);
        services.AddSingleton<IRelationshipGraph>(graph);
        services.AddSingleton(graph);
```

- [ ] **Step 7: Run — verify pass; full suite**

Run: `dotnet test --filter FullyQualifiedName~RelationScannerTests` → PASS (3). Then `dotnet test` → all green (graph builds at startup without throwing for the valid sample set).

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: scan relations and build validated relationship graph with descriptors"
```

---

### Task 4: Schema emission of relations

**Files:** none new (Relations populated + serialized by existing SchemaController).
**Test:** `tests/Struo.Tests/Api/RelationSchemaTests.cs`.

- [ ] **Step 1: Write the failing schema tests**

```csharp
// tests/Struo.Tests/Api/RelationSchemaTests.cs
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public class RelationSchemaTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Article_schema_includes_relations()
    {
        var client = _factory.CreateClient();
        var body = await (await client.GetAsync("/api/schema/article")).Content.ReadAsStringAsync();
        body.Should().Contain("\"relations\"");
        body.Should().Contain("\"name\":\"author\"");
        body.Should().Contain("\"kind\":\"manyToOne\"");
        body.Should().Contain("\"name\":\"tags\"");
        body.Should().Contain("\"kind\":\"manyToMany\"");
        body.Should().Contain("\"targetCollection\":\"author\"");
    }

    [Fact]
    public async Task Category_schema_includes_self_referencing_tree()
    {
        var client = _factory.CreateClient();
        var body = await (await client.GetAsync("/api/schema/category")).Content.ReadAsStringAsync();
        body.Should().Contain("\"name\":\"parent\"");
        body.Should().Contain("\"selfReferencing\":true");
        body.Should().Contain("\"name\":\"children\"");
    }
}
```

- [ ] **Step 2: Run — verify pass**

Run: `dotnet test --filter FullyQualifiedName~RelationSchemaTests` → PASS (2). (Relations is a public property on `CollectionMetadata`, serialized by default with the existing enum→camelCase converter.)

- [ ] **Step 3: Commit**

```bash
git add tests/Struo.Tests/Api/RelationSchemaTests.cs
git commit -m "test: verify schema emits relation metadata"
```

---

### Task 5: `deep` read expansion (batched stitching)

**Files:** Create `DeepSpec.cs`; modify `QueryModel.cs`, `StruoQueryOptions.cs`, `QueryParser.cs`, `IItemRepository.cs`, `SqlSugarItemRepository.cs`, `ItemService.cs`, `ItemsController.cs`, `DataServiceCollectionExtensions.cs`; create `RelationExpander.cs`.
**Test:** `tests/Struo.Tests/Query/DeepExpansionTests.cs`.

**Interfaces:**
- Consumes: `RelationshipGraph` (descriptors), `IEntityRegistry`, `IItemRepository`.
- Produces:
  - `DeepSpec`/`DeepRelationSpec` (Domain); `QueryModel.Deep`; `StruoQueryOptions.MaxRelationDepth`.
  - `IItemRepository.QueryWhereInAsync(string collection, string property, IReadOnlyList<object> values, CancellationToken)` and `QueryEntityWhereInAsync(Type entityType, string propertyName, IReadOnlyList<object> values, CancellationToken)`.
  - `RelationExpander.ExpandAsync(collection, parents, deep, projectTargetRow, ct)` → `parentId → (relationName → object?|list)`.

- [ ] **Step 1: Create `DeepSpec`, extend `QueryModel` + options**

```csharp
// src/Struo.Domain/Query/DeepSpec.cs
namespace Struo.Domain.Query;

public sealed record DeepRelationSpec(IReadOnlyList<string>? Fields, int? Limit);
public sealed record DeepSpec(IReadOnlyDictionary<string, DeepRelationSpec> Relations);
```

`QueryModel.cs` — add init property (keep the positional ctor):

```csharp
public sealed record QueryModel(
    IReadOnlyList<string>? Fields, FilterNode? Filter, IReadOnlyList<SortField> Sort,
    int Limit, int Offset, string? Search)
{
    public DeepSpec? Deep { get; init; }
}
```

`StruoQueryOptions.cs` — add: `public int MaxRelationDepth { get; set; } = 5;`

- [ ] **Step 2: Parse `deep` in `QueryParser`** (add helpers; thread into both parse methods)

```csharp
private static DeepSpec? ParseDeepEnvelope(JsonElement env)
{
    if (!env.TryGetProperty("deep", out var d) || d.ValueKind != JsonValueKind.Object) return null;
    var map = new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase);
    foreach (var rel in d.EnumerateObject())
    {
        IReadOnlyList<string>? fields = null; int? limit = null;
        if (rel.Value.ValueKind == JsonValueKind.Object)
        {
            if (rel.Value.TryGetProperty("fields", out var f) && f.ValueKind == JsonValueKind.Array)
                fields = f.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            if (rel.Value.TryGetProperty("limit", out var l) && l.TryGetInt32(out var li)) limit = li;
        }
        map[rel.Name] = new DeepRelationSpec(fields, limit);
    }
    return new DeepSpec(map);
}

private static DeepSpec? ParseDeepQueryString(IReadOnlyDictionary<string, string?> query)
{
    if (!query.TryGetValue("deep", out var dv) || string.IsNullOrWhiteSpace(dv)) return null;
    var map = dv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToDictionary(n => n, _ => new DeepRelationSpec(null, null), StringComparer.OrdinalIgnoreCase);
    return new DeepSpec(map);
}
```

`ParseEnvelope` ends with `return model with { Deep = ParseDeepEnvelope(env) };`; `ParseQueryString` with `return model with { Deep = ParseDeepQueryString(query) };`.

- [ ] **Step 3: Add repository where-IN helpers** (in `IItemRepository` + `SqlSugarItemRepository`)

Add to `IItemRepository`:

```csharp
Task<IReadOnlyList<object>> QueryWhereInAsync(string collection, string property, IReadOnlyList<object> values, CancellationToken ct = default);
Task<IReadOnlyList<object>> QueryEntityWhereInAsync(Type entityType, string propertyName, IReadOnlyList<object> values, CancellationToken ct = default);
Task SyncManyToManyAsync(Type junctionType, string parentFkProperty, string targetFkProperty, string? sortProperty, object parentId, IReadOnlyList<object> targetIds, CancellationToken ct = default);
```

Implement in `SqlSugarItemRepository` using the cached `MakeGenericMethod` pattern. For where-IN, resolve the column via `EntityMaintenance.GetDbColumnName(propertyName, type)` and run `db.Queryable<T>().In(column, values).ToListAsync()` (verify `.In(string, IEnumerable)` / use `ConditionalType.In` models if needed). `QueryWhereInAsync` maps `collection`→`EntityType` via the registry, then delegates to `QueryEntityWhereInAsync(type, prop, values)`. (SyncManyToManyAsync implemented in Task 6.)

> Empty `values` → return empty list without querying (avoid an `IN ()`).

- [ ] **Step 4: Implement `RelationExpander`** (M2O first; O2M; M2M)

```csharp
// src/Struo.Infrastructure/Query/RelationExpander.cs
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.Query;

public sealed class RelationExpander(IItemRepository repository, RelationshipGraph graph)
{
    // projectTarget: (targetCollection, targetRow, fields?) -> camelCase dict
    public async Task<Dictionary<object, Dictionary<string, object?>>> ExpandAsync(
        string collection, IReadOnlyList<object> parents, DeepSpec deep,
        Func<string, object, IReadOnlyList<string>?, IReadOnlyDictionary<string, object?>> projectTarget,
        Func<object, object> parentId, Func<object, string, object?> readProp,
        CancellationToken ct = default)
    {
        var result = new Dictionary<object, Dictionary<string, object?>>();
        foreach (var p in parents) result[parentId(p)] = new();

        foreach (var (relName, spec) in deep.Relations)
        {
            var desc = graph.Descriptors(collection).FirstOrDefault(d =>
                string.Equals(d.Meta.Name, relName, StringComparison.OrdinalIgnoreCase))
                ?? throw new QueryException($"Unknown relation '{relName}' on '{collection}'.");
            var rel = desc.Meta;

            switch (rel.Kind)
            {
                case RelationKind.ManyToOne:
                {
                    var fkValues = parents.Select(p => readProp(p, rel.ForeignKey!)).Where(v => v is not null).Distinct().ToList()!;
                    var targets = await repository.QueryWhereInAsync(rel.TargetCollection, "id", fkValues!, ct);
                    var byId = targets.ToDictionary(t => readProp(t, "id")!, t => t);
                    foreach (var p in parents)
                    {
                        var fk = readProp(p, rel.ForeignKey!);
                        result[parentId(p)][relName] = fk is not null && byId.TryGetValue(fk, out var tr)
                            ? projectTarget(rel.TargetCollection, tr, spec.Fields) : null;
                    }
                    break;
                }
                case RelationKind.OneToMany:
                {
                    var ids = parents.Select(parentId).ToList();
                    var children = await repository.QueryWhereInAsync(rel.TargetCollection, desc.ReverseForeignKeyProperty!, ids, ct);
                    var grouped = children.GroupBy(ch => readProp(ch, desc.ReverseForeignKeyProperty!)!);
                    foreach (var p in parents)
                    {
                        var pid = parentId(p);
                        var rows = grouped.FirstOrDefault(g => Equals(g.Key, pid))?.Select(ch => projectTarget(rel.TargetCollection, ch, spec.Fields)).ToList()
                                   ?? new List<IReadOnlyDictionary<string, object?>>();
                        result[pid][relName] = rows;
                    }
                    break;
                }
                case RelationKind.ManyToMany:
                {
                    var ids = parents.Select(parentId).ToList();
                    var junctions = await repository.QueryEntityWhereInAsync(desc.JunctionType!, desc.JunctionParentFk!, ids, ct);
                    var targetIds = junctions.Select(j => readProp(j, desc.JunctionTargetFk!)!).Distinct().ToList();
                    var targets = (await repository.QueryWhereInAsync(rel.TargetCollection, "id", targetIds, ct))
                        .ToDictionary(t => readProp(t, "id")!, t => t);
                    foreach (var p in parents)
                    {
                        var pid = parentId(p);
                        var rows = junctions
                            .Where(j => Equals(readProp(j, desc.JunctionParentFk!), pid))
                            .OrderBy(j => desc.JunctionSort is null ? 0 : Convert.ToInt32(readProp(j, desc.JunctionSort)))
                            .Select(j => readProp(j, desc.JunctionTargetFk!)!)
                            .Where(tid => targets.ContainsKey(tid))
                            .Select(tid => projectTarget(rel.TargetCollection, targets[tid], spec.Fields))
                            .ToList();
                        result[pid][relName] = rows;
                    }
                    break;
                }
            }
        }
        return result;
    }
}
```

> `readProp` reads a property by CLR/camel name off an entity object; `projectTarget` reuses `ItemService`'s metadata projection for the target collection (pass a delegate). `JunctionParentFk`/`JunctionTargetFk` are the junction's CLR property names (e.g. `ArticleId`/`TagId`); `readProp` must accept those. Wire these via the `ItemService`. Implement M2O, get its test green, commit; then O2M; then M2M (M2M test arrives in Task 6).

- [ ] **Step 5: Wire `ItemService` + controller + DI; validate deep**

- `AddStruoData`: `services.AddScoped<RelationExpander>();`.
- `ItemService`: inject `IRelationshipGraph` + `RelationExpander`. Add `DeepSpec? deep` to `GetAsync` and read `query.Deep` in `QueryAsync`. **Validate** each deep relation via `graph.Resolve` (else `QueryException`→400). After projecting scalar rows, if `deep` present, call `RelationExpander.ExpandAsync(...)` passing a `projectTarget` delegate (`(targetColl, row, fields) => ProjectFor(targetColl, row, fields)` — a refactor of the existing `Project` to take a collection + entity) and `readProp`/`parentId` delegates built from the target/parent `EntityDescriptor`. Merge each parent's relation map into its projected dict.
- `ItemsController`: `GET /{id}` reads `deep` from `Request.Query` and passes a `DeepSpec?` to `GetAsync`; `List`/`Query` already carry `Deep` in the parsed `QueryModel`.

- [ ] **Step 6: Write the failing deep tests** (M2O + validation + no-auto-load)

```csharp
// tests/Struo.Tests/Query/DeepExpansionTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class DeepExpansionTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    [Fact]
    public async Task Deep_expands_m2o_author()
    {
        var c = _factory.CreateClient();
        var authorId = Root(await (await c.PostAsJsonAsync("/api/items/author", new { name = "Ada" })).Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetInt64();
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", new { title = "T", status = "draft", authorId })).Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetInt64();

        var resp = await c.GetAsync($"/api/items/article/{id}?deep=author");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("author").GetProperty("name").GetString().Should().Be("Ada");
    }

    [Fact]
    public async Task Deep_unknown_relation_returns_400()
    {
        var c = _factory.CreateClient();
        var authorId = Root(await (await c.PostAsJsonAsync("/api/items/author", new { name = "X" })).Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetInt64();
        var id = Root(await (await c.PostAsJsonAsync("/api/items/article", new { title = "Y", status = "draft", authorId })).Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetInt64();
        (await c.GetAsync($"/api/items/article/{id}?deep=ghostrel")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_without_deep_has_no_relation_keys()
    {
        var c = _factory.CreateClient();
        var data = Root(await (await c.GetAsync("/api/items/article?limit=1")).Content.ReadAsStringAsync()).GetProperty("data");
        if (data.GetArrayLength() > 0) data[0].TryGetProperty("author", out _).Should().BeFalse();
    }
}
```

- [ ] **Step 7: Run — verify pass; full suite**

Run: `dotnet test --filter FullyQualifiedName~DeepExpansionTests` → PASS. Then `dotnet test` → all green.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: add deep relation expansion via batched stitching"
```

---

### Task 6: M2M assignment writes

**Files:** modify `SqlSugarItemRepository.cs` (`SyncManyToManyAsync`), `ItemService.cs` (read `tags` ids from body, sync after persist).
**Test:** `tests/Struo.Tests/Query/RelationWriteTests.cs`.

- [ ] **Step 1: Write the failing M2M write test**

```csharp
// tests/Struo.Tests/Query/RelationWriteTests.cs
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

[Collection("ApiIntegration")]
public class RelationWriteTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;
    private static async Task<long> Id(System.Net.Http.HttpResponseMessage r) =>
        Root(await r.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetInt64();

    [Fact]
    public async Task Create_article_with_tags_syncs_junction_and_deep_reads_them()
    {
        var c = _factory.CreateClient();
        var authorId = await Id(await c.PostAsJsonAsync("/api/items/author", new { name = "A" }));
        var t1 = await Id(await c.PostAsJsonAsync("/api/items/tag", new { name = "t1" }));
        var t2 = await Id(await c.PostAsJsonAsync("/api/items/tag", new { name = "t2" }));

        var id = await Id(await c.PostAsJsonAsync("/api/items/article", new { title = "M2M", status = "draft", authorId, tags = new[] { t1, t2 } }));

        var tags = Root(await (await c.GetAsync($"/api/items/article/{id}?deep=tags")).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("tags");
        tags.GetArrayLength().Should().Be(2);
    }
}
```

- [ ] **Step 2: Run — verify fail**

Run: `dotnet test --filter FullyQualifiedName~RelationWriteTests`
Expected: FAIL — tags not persisted.

- [ ] **Step 3: Implement `SyncManyToManyAsync` + ItemService wiring**

`SqlSugarItemRepository.SyncManyToManyAsync(junctionType, parentFkProperty, targetFkProperty, sortProperty, parentId, targetIds, ct)`: via `MakeGenericMethod(junctionType)`, delete existing junction rows where parentFk == parentId, then insert one row per target id (in order; set sort = index if `sortProperty` non-null). Use SqlSugar `Deleteable<TJunction>().Where(conditionals)` and `Insertable(list)` (by-type generic). Construct junction instances via `Activator.CreateInstance` + reflection set of the FK/sort properties.

`ItemService.CreateAsync`/`UpdateAsync`: after persisting the entity, for each M2M relation in `graph.Relations(collection)`, read the incoming array from the original JSON body under the relation name (e.g. `tags`); if present, validate each id exists in the target collection (`QueryWhereInAsync(target,"id",ids)` count == ids count, else `QueryException`→400), then call `repository.SyncManyToManyAsync(...)` with the junction descriptor from `graph.Descriptors(collection)`.

- [ ] **Step 4: Run — verify pass; full suite**

Run: `dotnet test --filter FullyQualifiedName~RelationWriteTests` → PASS. Then `dotnet test` → all green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: M2M assignment writes sync the junction table"
```

---

### Task 7: OnDelete Restrict enforcement

**Files:** create `RelationConflictException.cs`; modify `Program.cs` (409 middleware), `ItemService.cs` (Restrict check).
**Test:** append to `RelationWriteTests.cs`.

- [ ] **Step 1: Create the exception + 409 middleware mapping**

```csharp
// src/Struo.Domain/Query/RelationConflictException.cs
namespace Struo.Domain.Query;

public sealed class RelationConflictException(string message) : Exception(message);
```

In `Program.cs` exception middleware, add (with the `HasStarted` guard, mirroring the others):

```csharp
        catch (Struo.Domain.Query.RelationConflictException ex)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new { error = new { message = ex.Message } });
            }
        }
```

- [ ] **Step 2: Write the failing Restrict test** (append)

```csharp
    [Fact]
    public async Task Delete_author_referenced_by_article_is_blocked_409()
    {
        var c = _factory.CreateClient();
        var authorId = await Id(await c.PostAsJsonAsync("/api/items/author", new { name = "Ref" }));
        await c.PostAsJsonAsync("/api/items/article", new { title = "R", status = "draft", authorId });

        (await c.DeleteAsync($"/api/items/author/{authorId}")).StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
    }
```

- [ ] **Step 3: Run — verify fail**

Run: `dotnet test --filter FullyQualifiedName~RelationWriteTests`
Expected: FAIL — delete returns 204, not 409.

- [ ] **Step 4: Implement the Restrict check in `ItemService.DeleteAsync`**

Before `repository.DeleteAsync`: `foreach (var (source, fk) in graph.InboundRestrict(collection))` → `var refs = await repository.QueryWhereInAsync(source, fk, [convertedId], ct);` if `refs.Count > 0` → `throw new RelationConflictException($"Cannot delete '{collection}/{id}': referenced by '{source}'.");`. (The id must be converted to the PK type for the FK comparison — reuse the repository's id handling or pass the raw string id and let `QueryWhereInAsync` coerce; ensure type match with the FK column.)

> `QueryWhereInAsync` compares the FK column to the id value; ensure the id is coerced to the FK's CLR type (long). If needed, add an overload or coerce in `ItemService` using the target `EntityDescriptor` PK type.

- [ ] **Step 5: Run — verify pass; full suite**

Run: `dotnet test --filter FullyQualifiedName~RelationWriteTests` → PASS. Then `dotnet test` → all green.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: enforce OnDelete Restrict (409) on referenced rows"
```

---

## Self-Review

**1. Spec coverage** (design → tasks):

| Spec item | Task |
|---|---|
| Blog relation sample set (M2O/O2M/M2M/self-ref) | Task 1 |
| RelationMetadata + RelationKind (Domain) | Task 2 |
| Relation scanner + graph + consistency validation (§8) | Task 3 |
| Schema emits relations | Task 4 |
| deep/Includes read expansion + depth cap (§7.4/§8) | Task 5 |
| M2M assignment writes | Task 6 |
| OnDelete Restrict (409) | Task 7 |
| No auto-load on lists (§8) | Task 5 (test) |
| §11 polymorphic note | spec only (documentation; no code) |
| Cross-relation filter/sort | DEFERRED → Phase 3b |

`MaxRelationDepth` cap: Task 5 (validate deep depth). No gaps for 3a scope.

**2. Placeholder scan:** No "TBD". The two largest tasks (5 deep, 6 M2M) name exact sub-pieces and an incremental commit order (M2O→O2M→M2M). SqlSugar `NavigateAttribute` accessor names and `Queryable.In`/`ToListAsync`/junction CRUD are flagged for package verification per §15 and caught by TDD.

**3. Type consistency:** `RelationKind`/`RelationMetadata`/`CollectionMetadata.Relations` (T2); `MetadataScanner.ScanRelations`, `IRelationshipGraph.Relations/Resolve/InboundRestrict`, `RelationshipGraph.Descriptors`, `RelationDescriptor` (T3, T5, T6, T7); `DeepSpec`/`DeepRelationSpec`/`QueryModel.Deep`/`StruoQueryOptions.MaxRelationDepth` (T5); `RelationExpander.ExpandAsync`, `IItemRepository.QueryWhereInAsync`/`QueryEntityWhereInAsync`/`SyncManyToManyAsync` (T5, T6); `RelationConflictException` (T7) — used consistently across tasks.

**Known verify-against-package points (flagged inline):** SqlSugar `NavigateAttribute` members (FK name + M2M mapping type/A/B) — Task 3, verify or read `CustomAttributeData`; `Queryable<T>().In(string, values)` + `ToListAsync` — Task 5; junction `Deleteable`/`Insertable` by type — Task 6. The 3b cross-relation join engine is a separate spike-first cycle.
