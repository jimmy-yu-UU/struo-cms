# StruoCMS — Phase 4 (i18n / Translations) Design

> Date: 2026-06-26
> Source of truth: the StruoCMS master spec (§4 `[CmsTranslations]`, §18). Covers **Phase 4 only**.
> Builds on Phases 0–3 (merged). Activates the Phase-1 `CmsTranslationsAttribute` (defined, not yet read).

## 1. Goal & scope

Make `Translatable` fields multilingual via a **sidecar translation entity per collection**. Translatable
fields live **only** in the translation sub-table (not on the parent). Reads return **all locales** by
default (frontend owns fallback); an optional `?locale=` filters to one. Writes embed `translations` in the
item body. Available languages are a framework-owned, CRUD-able **`Language` collection** (the single source
of truth for which locales the admin UI may edit).

**In scope (4):**
- `[CmsTranslations(typeof(T))]` scanning → `TranslationMetadata`; startup consistency validation (fail-fast).
- Translatable field metadata sourced from the translation entity and merged into the parent collection.
- Framework built-in `Language` collection (code/name/isDefault/enabled/sort), dev-seeded (`en` default + `zh-TW`).
- Read: all-locales projection under `translations`; `?locale=` single-locale filter; locale validated against enabled `Language`.
- Write: embedded `translations` per-locale **upsert** (partial update preserves other locales).
- Schema marks translatable fields; languages exposed via `GET /api/items/language`.
- Cached `ILanguageProvider` (invalidated on `Language` writes).

**Out of scope (deferred / rejected):**
- Backend fallback/merge across locales (frontend's responsibility).
- Translatable relations, option labels, or SEO fields.
- Per-locale workflow/publishing/review; locale-scoped permissions.
- Admin UI (Phase 7); files (Phase 5).
- A separate translations CRUD endpoint (sidecar is not a queryable collection).

## 2. Decisions locked in brainstorming

| Topic | Decision |
|---|---|
| Storage | Sidecar translation entity per collection (`[CmsTranslations(typeof(T))]`) |
| Field home | Translatable fields declared **only on the translation entity**; parent keeps a `Translations` nav only |
| Read | All locales by default under `translations`; `?locale=` filters to one; **no backend fallback** |
| Languages | Framework-owned CRUD `Language` collection is the source of truth (not a config list) |
| Default locale | The `Language` row with `IsDefault=true` (used by the frontend for fallback only) |
| Write | Embedded `translations` in item body; per-locale upsert; partial update |
| Language home | `Language` defined in `Struo.Infrastructure` (framework built-in), auto-registered |

## 3. Layer placement (§2)

- **Domain**: `TranslationMetadata` record; `CollectionMetadata.Translation` (nullable). Domain stays
  dependency-free (no SqlSugar). `LanguageInfo` value record for the provider.
- **Application**: `ILanguageProvider` (enabled languages + default code; cached); `ITranslationReader`/
  write-sync ports; `ItemService` overlay + write integration; locale validation.
- **Infrastructure**: `Language` entity (`[CmsCollection]`+`[SugarTable]`); scanner extension
  (`[CmsTranslations]` → metadata, field merge, fail-fast validation); built-in-collection registration so
  the scan includes `Language`; translation batch-load + per-locale sync (SqlSugar); `LanguageProvider` impl.
- **Api**: items endpoints accept `?locale=`; controllers pass it through. No new endpoints (Language uses
  the existing items endpoints).

## 4. Model — fields only in the sub-table

Parent declares a translations nav (property-level attribute) and **no** translatable scalar columns:
```csharp
// Article (sample) — Title/Body removed from the parent
[CmsField(Label="Status", Interface=Select, Sort=3, Group="Content")] public string Status {...}
[CmsField(Label="Published At", Interface=DateTime, Sort=4, Group="Content")] public DateTime? PublishedAt {...}
// relations, SEO, audit unchanged

[CmsTranslations(typeof(ArticleTranslation))]
[SugarColumn(IsIgnore = true)]
public List<ArticleTranslation> Translations { get; set; } = [];
```
Translation entity owns the translatable fields:
```csharp
[SugarTable("article_translations")]
public sealed class ArticleTranslation {
  [SugarColumn(IsPrimaryKey=true, IsIdentity=true)] public long Id { get; set; }
  public long ArticleId { get; set; }            // FK convention: {Parent}Id
  public string Locale { get; set; } = "";        // matches an enabled Language.Code
  [CmsField(Label="Title", Interface=Text, Required=true, Searchable=true, Sort=1, Group="Content")]
  public string Title { get; set; } = "";
  [CmsField(Label="Body", Interface=RichText, Sort=2, Group="Content")]
  public string? Body { get; set; }
}
```
Logical unique key `(ArticleId, Locale)` is enforced by the per-locale upsert (InitTables adds no constraint).
The translation entity is **not** a CMS collection (like the M2M junction) — not registered, not queryable.

## 5. Metadata & scanner (§4)

- A collection carrying `[CmsTranslations(typeof(T))]` gets `TranslationMetadata`:
  ```
  sealed record TranslationMetadata {
    Type TranslationEntityType;   // ArticleTranslation
    string ForeignKeyProperty;    // "ArticleId" (CLR); camel "articleId"
    string LocaleProperty;        // "Locale"
    IReadOnlyList<string> Fields; // camelCase translatable field names: ["title","body"]
  }
  // CollectionMetadata gains: TranslationMetadata? Translation
  ```
- The collection's field list = parent non-translatable `[CmsField]` **+** translation-entity `[CmsField]`
  (each merged with `Translatable=true`; `Id`/FK/`Locale` excluded). Field `Sort`/`Group` honored.
- **Startup consistency validation (fail-fast `MetadataException`):** translation entity has an `Id` PK, the
  `{Parent}Id` FK property (type matches parent PK), a `string Locale` property, and ≥1 `[CmsField]`.

## 6. `Language` collection (framework built-in)

```csharp
[CmsCollection("Language", Group="System", DefaultDisplayField=nameof(Name))]
[SugarTable("languages")]
public sealed class Language : IAuditable {
  [SugarColumn(IsPrimaryKey=true, IsIdentity=true)] public long Id { get; set; }
  [CmsField(Label="Code", Interface=Text, Required=true, Searchable=true, Sort=1)] public string Code {...}   // "en","zh-TW"
  [CmsField(Label="Name", Interface=Text, Required=true, Sort=2)] public string Name {...}                    // "English","繁體中文"
  [CmsField(Label="Default", Interface=Boolean, Sort=3)] public bool IsDefault { get; set; }
  [CmsField(Label="Enabled", Interface=Boolean, Sort=4)] public bool Enabled { get; set; } = true;
  [CmsField(Label="Sort", Interface=Number, Sort=5)] public int Sort { get; set; }
  // IAuditable
}
```
- Lives in `Struo.Infrastructure`; the metadata scan/registry includes framework built-in collections so
  `language` is a normal CRUD collection via the existing items endpoints.
- Dev `InitTables` seeds `en` (`IsDefault=true`) and `zh-TW` if the table is empty.
- `ILanguageProvider`: `IReadOnlyList<LanguageInfo> Enabled()`, `string DefaultCode()`. Cached; invalidated
  when any `language` row is created/updated/deleted (ItemService write path triggers invalidation).

## 7. Read execution (batched, N+1-safe)

- `ItemService.QueryAsync`/`GetAsync` accept `locale?` (from `?locale=`).
- If `locale` is provided and is not an enabled `Language.Code` → 400.
- After projecting parent rows, for a collection with `Translation` metadata: one batched query loads
  translation rows where `ForeignKey IN (parent ids)` and (if `locale` given) `Locale = locale`, else all
  enabled locales. Group by parent id → build `translations: { locale: { field: value } }` (translatable
  fields only, honoring the field whitelist/readable rules). Attach to each projected row.
- No fallback/merge; absent locales simply absent. List endpoints include `translations` too.

Projected shape:
```json
{ "id":1, "status":"draft", "publishedAt":null,
  "translations": { "en": {"title":"Hello","body":"..."}, "zh-TW": {"title":"你好","body":"..."} } }
```

## 8. Write execution (embedded, per-locale upsert)

- Body: non-translatable fields top-level + `translations: { "en":{title,body}, "zh-TW":{...} }`.
- `ItemService` strips `translations` before deserializing the parent (like M2M keys); creates/updates the parent.
- For each provided locale: validate it is an enabled `Language.Code` (else 400); validate keys ⊆ translatable
  fields (else 400). **Upsert** `(parentId, locale)`: delete that row then insert with the provided field
  values. Locales not in the body are left untouched (partial update).
- **Required:** on create, the **default-locale** translation must be present and its `Required` translatable
  fields non-empty; for any provided locale, its `Required` fields must be non-empty.
- Repository `SyncTranslationsAsync(translationType, fkProperty, localeProperty, fieldProperties, parentId,
  perLocaleValues, ct)` — generic, transaction-wrapped, per-locale upsert.

## 9. Configuration

No new options section. The `Language` collection is the source of truth for locales and default. (The
earlier-considered `StruoLocalizationOptions` config list is **dropped** in favor of the collection.)

## 10. Error handling

- Read/write `locale` not an enabled `Language.Code` → 400 (`QueryException`).
- `translations` contains a non-translatable field key → 400.
- A provided locale missing a `Required` translatable field → 400; create missing the default-locale
  translation → 400.
- Misconfigured translation entity → startup fail-fast (`MetadataException`).

## 11. Testing (TDD)

- **Scanner unit**: `[CmsTranslations]` → `TranslationMetadata` (entity, FK, locale, fields); collection field
  list merges translatable fields from the sub-table marked `Translatable`; broken translation entity throws
  at scan.
- **Language collection**: CRUD via items endpoints; dev seed present (`en` default + `zh-TW`).
- **Read (SQLite + Blog)**: create article with `en`+`zh-TW` translations; GET without `locale` → both under
  `translations`; GET `?locale=zh-TW` → only that locale; unknown locale → 400; list endpoint includes
  `translations`.
- **Write**: nested `translations` persist + round-trip; partial update touches only the provided locale
  (others preserved); non-translatable key → 400; unknown locale → 400; create without default-locale → 400.
- **Schema**: `/api/schema/article` field list includes translatable `title`/`body` with `translatable:true`.

## 12. Verification gate (§18 — Phase 4)

- build/test green (SQLite).
- `Language` CRUD + dev seed.
- Read returns all locales by default and a single locale with `?locale=`; unknown locale → 400.
- Write persists embedded translations; partial update preserves other locales.
- Schema marks translatable fields.
- **Live PostgreSQL** re-exercised: seed a language, create a multi-locale article, read all/single locale,
  partial-update one locale, and confirm the 400 validations.

## 13. Package & environment rules (§15)

All DB access via SqlSugar ORM (batched `IN` loads, per-locale upsert in a transaction); zero vendor SQL.
Domain stays dependency-free. Packages via `dotnet add package` (latest, CPM).
