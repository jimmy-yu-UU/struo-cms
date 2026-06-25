# StruoCMS — Phase 1 (Metadata Core) Design

> Date: 2026-06-25
> Source of truth: the StruoCMS master spec (§3, §4, §6, §9, §18). This document covers **Phase 1 only**.
> Builds on Phase 0 (foundation), already merged to `main`.

## 1. Goal & scope

Make the developer's annotated entity classes the single source of truth for backend UI metadata. Scan `[Cms*]`-annotated entities once at startup into an immutable, cached registry, and expose it via `GET /api/schema`. This metadata later drives CRUD, the query DSL, and the Vue admin.

**In scope (master spec §18, Phase 1):**
- §4 attribute set + §6 interface catalogs + field groups
- Startup scanner → cached registry (`IMetadataProvider`); no per-request reflection
- `GET /api/schema` (aggregate) + `GET /api/schema/{collection}` (per-collection)
- §9 SEO field group via `ISeoMeta` (auto-applied by convention)
- Audit fields (§5) surfaced as read-only system fields

**Out of scope (deferred):** relation metadata emission + relation entities & graph (Phase 3); translation tables/`[CmsTranslations]` handling (Phase 4 — but the `Translatable` flag IS emitted); files for `SeoOgImageId` (Phase 5 — stays scalar `long?`); generic CRUD + DSL (Phase 2).

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| Relations | Define `CmsRelationAttribute` + `RelationInterface` + `OnDelete` now; do NOT build relation entities or emit relation metadata into schema until Phase 3 |
| Translations | Define `CmsTranslationsAttribute` now; emit the `Translatable` field flag; defer translation-table handling to Phase 4 |
| Sample scope | Enrich `Article` + add a 2nd standalone collection `Tag` (no relations) to prove a multi-collection registry |
| Schema API | `GET /api/schema` (all) + `GET /api/schema/{collection}` (one, 404 unknown) |
| Naming | Schema collection & field `name` are **camelCase** (`Article`→`article`, `Title`→`title`); registry retains `PropertyInfo` for Phase-2 DSL resolution; lookups case-insensitive |
| Discovery | `AddStruoMetadata(params Assembly[])` called from the Api composition root; framework stays decoupled from `samples/*` |
| Scan timing | Eager scan at registration → immutable singleton; fail-fast at startup on invalid metadata; zero per-request reflection |

## 3. Layer placement (§2)

- **Domain** (pure): `[Cms*]` attributes; `FieldInterface`/`RelationInterface`/`OnDelete` enums; metadata model records (`CollectionMetadata`, `FieldMetadata`, `FieldOption`, `FieldGroupMetadata`); `ISeoMeta`.
- **Application**: `IMetadataProvider` port; `SchemaService` use case.
- **Infrastructure**: `MetadataScanner` (reflection); `CachedMetadataProvider` (immutable registry); `AddStruoMetadata` DI extension.
- **Api**: `SchemaController`.

Domain remains free of external packages (attributes are plain C# attributes; models are plain records).

## 4. Attributes & enums (§4, §6)

Domain `Struo.Domain/Metadata/Attributes`:
- `CmsCollectionAttribute(string label)` — `Label`, `Icon?`, `Group?`, `DefaultDisplayField?`. `[AttributeUsage(Class)]`.
- `CmsFieldAttribute` — `Label`, `Interface` (FieldInterface), `Display?`, `Required` (bool), `Searchable` (bool), `Sortable` (bool), `Sort` (int), `ReadOnly` (bool), `Hidden` (bool), `HelpText?`, `Translatable` (bool), `Group?`. `[AttributeUsage(Property)]`.
- `CmsOptionsAttribute(params string[] options)` — each `"value:label"`. `[AttributeUsage(Property)]`.
- `CmsFieldGroupAttribute(string name)` — `Label?`, `Sort` (int). `[AttributeUsage(Class, AllowMultiple=true)]` (declares UI panels for a collection).
- `CmsRelationAttribute` — `Interface` (RelationInterface), `DisplayTemplate?`, `PickerQuery?`, `SortField?`, `OnDelete` (OnDelete), `Editable` (bool), `DisplayColumns?`, `MaxDepth` (int). **Defined only; not read by the Phase 1 scanner.**
- `CmsTranslationsAttribute(Type translationEntityType)`. **Defined only; not read by the Phase 1 scanner.**

Domain `Struo.Domain/Metadata/Enums`:
- `FieldInterface` — full §6 catalog: `Text, Textarea, RichText, Markdown, Code, Slug, Email, Url, Password, Color, Phone, Number, Slider, Rating, Boolean, Checkbox, Date, Time, DateTime, Select, MultiSelect, Radio, CheckboxGroup, Tags, Json, KeyValue, Repeater, File, Image, Files, Hidden, Divider, Uuid`.
- `RelationInterface` — `Dropdown, TagSelect, TreeSelect, RelatedList`.
- `OnDelete` — `Restrict, Cascade, SetNull`.

Option-type interfaces (validated against `CmsOptions`): `Select, MultiSelect, Radio, CheckboxGroup, Tags`.

## 5. Metadata models (Domain, immutable records)

```
CollectionMetadata {
  string Name;            // camelCase entity name, e.g. "article"
  string Label;           // from CmsCollection
  string? Icon;
  string? Group;
  string? DefaultDisplayField;   // camelCase field name
  IReadOnlyList<FieldGroupMetadata> FieldGroups;
  IReadOnlyList<FieldMetadata> Fields;
}
FieldMetadata {
  string Name;            // camelCase property name, e.g. "title"
  string Label;
  FieldInterface Interface;
  bool Required; bool Searchable; bool Sortable; bool ReadOnly; bool Hidden;
  bool Translatable;
  int Sort;
  string? HelpText; string? Group;
  IReadOnlyList<FieldOption>? Options;   // null unless option-type
  bool IsSystem;          // true for IAuditable fields
}
FieldOption { string Value; string Label; }
FieldGroupMetadata { string Name; string? Label; int Sort; }
```

The registry additionally retains, internally (not serialized), each field's `PropertyInfo` for Phase-2 DSL resolution. The serialized projection is the four records above (camelCase JSON).

## 6. Scanner & cached registry (the core, §3)

- `AddStruoMetadata(this IServiceCollection, params Assembly[] assemblies)` runs `MetadataScanner.Scan(assemblies)` **eagerly during registration**, producing an immutable registry, and registers a populated `CachedMetadataProvider` as a singleton instance.
- `MetadataScanner.Scan`:
  1. Find all `[CmsCollection]` types across the given assemblies.
  2. For each, read class + property attributes → build `CollectionMetadata`.
  3. Emit `IAuditable` properties as `IsSystem=true, ReadOnly=true` fields (default interfaces: `DateTime`→DateTime, `string?`→Text).
  4. If the type implements `ISeoMeta`, inject the built-in **SEO** field group + three fields (`seoTitle`→Text, `seoMetaDescription`→Textarea, `seoOgImageId`→Number), Group="SEO".
  5. Order fields by `Sort` then declaration order.
- **Validation (throws at startup):** duplicate collection names (case-insensitive); `DefaultDisplayField` not a known field; `CmsOptions` on a non-option interface; `CmsOptions` entry not in `value:label` form.

`IMetadataProvider` (Application port):
```
IReadOnlyList<CollectionMetadata> GetCollections();
CollectionMetadata? GetCollection(string name);   // case-insensitive
```
`CachedMetadataProvider` holds the pre-built immutable list + a case-insensitive dictionary. No reflection after construction.

## 7. SEO field group via ISeoMeta (§9)

`Struo.Domain/Seo/ISeoMeta`:
```
string? SeoTitle { get; set; }
string? SeoMetaDescription { get; set; }
long? SeoOgImageId { get; set; }   // FK to files in Phase 5; scalar long? for now
```
The entity declares `: ISeoMeta` and the three properties (the "fragment"); SqlSugar maps the columns by convention. The CMS field metadata for them is the framework's built-in convention applied by the scanner — not re-declared on the entity, not a base class.

## 8. API surface

- `SchemaController` → `GET /api/schema` returns `IReadOnlyList<CollectionMetadata>`; `GET /api/schema/{collection}` returns one `CollectionMetadata` or 404.
- Backed by `SchemaService` (Application) delegating to `IMetadataProvider`. camelCase JSON (§1).

## 9. Data flow

1. Startup: `AddStruoMetadata(typeof(Article).Assembly)` scans → immutable registry → singleton.
2. Request: `SchemaController` → `SchemaService` → `IMetadataProvider` (in-memory lookup) → camelCase JSON. No reflection.

## 10. Error handling

- Invalid metadata fails fast at startup (during `AddStruoMetadata`) with a clear message naming the offending type/field.
- `GET /api/schema/{collection}` unknown name → 404.
- Unified error envelope is Phase 9; Phase 1 uses framework defaults for the 404.

## 11. Testing (TDD)

Unit (Infrastructure scanner):
- Article scan: collection name `article`, label, group, defaultDisplayField; fields with correct interfaces, Required/Searchable/Translatable/Sort/Group.
- `Status` `CmsOptions` → `options[]` of `{value,label}`; option-type validation.
- `IAuditable` fields present as `isSystem`/`readOnly`.
- `ISeoMeta` injects SEO group + three fields with the convention interfaces.
- `Tag` collection present (multi-collection registry).
- Validation throws: duplicate collection name; bad `DefaultDisplayField`; `CmsOptions` on non-option interface.
- Caching: a scan-count probe proves `Scan` runs once; `GetCollection` returns the cached instance without re-scanning.

Integration (Api, WebApplicationFactory):
- `GET /api/schema` → 200, contains `article` and `tag`.
- `GET /api/schema/article` → 200, includes SEO group + options + translatable markers, camelCase keys.
- `GET /api/schema/unknown` → 404.

## 12. Verification gate (§18 Phase 1)

- `dotnet build` clean (warnings-as-errors) + `dotnet test` green.
- `GET /api/schema` returns correct metadata incl. group, interface type, translatable markers, options, system fields, and the auto-applied SEO group for Article.
- Multi-collection (Article + Tag) present.
- Caching proven (single scan; no per-request reflection).

## 13. Package & environment rules (§15)

- Any new package via `dotnet add package` (latest, CPM). No DB access in this phase beyond what Phase 0 established. Domain stays dependency-free.
