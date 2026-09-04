# 6. Internationalization

## Two locale concepts — do not conflate them

StruoCMS has two independent notions of "locale," and they are governed by completely different
mechanisms:

| Concept | What it controls | Source of truth | Where it lives |
|---|---|---|---|
| **Content language** | Which per-locale copy of a translatable field a read/write targets (e.g. a `Title` in `en` vs `zh-TW`) | The `languages` table (a real `[CmsCollection]`, chapter 4) | `?locale=` query parameter on reads; a `translations` object keyed by locale on writes |
| **Admin UI locale** | Which language the admin SPA's own interface strings render in (menu labels, buttons, validation messages) | `frontend/src/i18n` (vue-i18n, `legacy: false`), switched by `UiLanguageSwitcher.vue` and persisted in the `uiLocaleStore` Pinia store | Entirely client-side; never sent to the API and never touches the `languages` table |

These are unrelated on purpose: an administrator can browse the admin SPA in `zh-TW` UI chrome
while editing `en` and `zh-TW` content side by side, or vice versa. Nothing in this chapter's
`?locale=`/`translations` mechanism has any bearing on which language the admin SPA's own labels
render in.

## The `languages` table and `GET /api/languages`

Content languages are rows in the framework's own `Language` entity
(`src/Struo.Infrastructure/Localization/Language.cs`) — `Language` is itself a real
`[CmsCollection]` (`Group = "System"`), so it is editable through the ordinary generic items API
(`/api/items/language`) exactly like any other collection, in addition to its own read endpoint.
Each row carries `code` (e.g. `"en"`, `"zh-TW"`), `name`, `isDefault`, `enabled`, and `sort`. A
fresh install seeds exactly two enabled languages — `en` (`isDefault = true`) and `zh-TW` — from
`LanguageSeeder`.

`GET /api/languages` (`LanguagesController`, `[Authorize(AuthenticationSchemes =
AuthSchemes.CookieOrBearer)]` — any authenticated caller, not just an admin) returns only the
*enabled* languages, projected to `{ code, name, isDefault }`:

```
$ curl -s -b cookies.txt http://localhost:5221/api/languages
{"success":true,"data":[{"code":"en","name":"English","isDefault":true},{"code":"zh-TW","name":"繁體中文","isDefault":false}]}
```

`ILanguageProvider` (`src/Struo.Infrastructure/Localization/LanguageProvider.cs`) caches this
enabled-set in memory per scope and exposes `DefaultCode()` (the row with `isDefault`, falling back
to the first enabled row, falling back to the literal `"en"` if somehow none are enabled) and
`IsEnabled(code)` (case-insensitive). Every locale-validity check described below — on read, on
write, on translation-object keys — resolves through this same provider, so disabling a language
row (or leaving `isDefault` unset entirely) takes effect immediately for every collection with a
translation sidecar, without a restart.

## Making fields translatable

A field becomes per-locale by setting `[CmsField(Translatable = true)]` on a property of the
**translation sidecar** entity (below) — never on the parent entity itself. `Translatable` fields
are skipped by the parent-row `Required` check (`ItemDeserializer.Deserialize` filters
`!f.Translatable` before validating `Required`) and are instead validated per-locale inside the
sidecar sync (next section). `MetadataScanner.ScanTranslations` folds each sidecar field into the
parent `CollectionMetadata.Fields` list too (marked `Translatable = true`), so a translatable field
*is* filterable/sortable through the ordinary query DSL allowlist like any other own-field
(`QueryValidator.Validate`'s field allowlist is built from all non-`Hidden` `meta.Fields`, with no
`Translatable` exclusion) — it just resolves against the sidecar table instead of the parent row, at
the effective query locale, via `RelationFilterResolver.IsTranslatableField`/`ResolveTranslatableIdsAsync`
(`src/Struo.Infrastructure/Query/RelationFilterResolver.cs`, chapter 7). Live-verified: filtering
`file` by its translatable `title` succeeds with no `?locale=` supplied at all (the effective locale
then defaults to `DefaultCode()`, `ItemService.QueryAsync`'s `queryLocale` default):

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file?filter%5Btitle%5D%5B_eq%5D=alpha-report"
{"success":true,"data":[{"id":"...","fileName":"alpha-report.txt", ...}],"meta":{"total":1,"limit":25,"offset":0}}
```

## The translation sidecar entity (complete example)

The framework's own `File` collection is a complete, live example: its `title`/`alt` fields are
translatable, so they live on `FileTranslation` — a plain SqlSugar entity, not itself a
`[CmsCollection]` — linked back to `File` via `[CmsTranslations(typeof(FileTranslation))]`:

```csharp
// src/Struo.Infrastructure/Files/File.cs (excerpt)
[CmsTranslations(typeof(FileTranslation))]
[SugarColumn(IsIgnore = true)]
public List<FileTranslation> Translations { get; set; } = [];
```

```csharp
// src/Struo.Infrastructure/Files/FileTranslation.cs
[SugarTable("file_translations")]
public sealed class FileTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    // FileId/Locale carry no unique attribute here: SqlSugarClientFactory's EntityService hook reads
    // this table's [CmsTranslations] metadata through TranslationSidecarIndexPolicy and adds the
    // composite unique (fileid, locale) to the generated column model at CodeFirst time.
    // SchemaGuard.AssertCriticalConstraintsAsync re-checks the resulting index in Development.
    public Guid FileId { get; set; }
    public string Locale { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Title", Interface = FieldInterface.Text, Searchable = true, Sort = 1)]
    public string? Title { get; set; }
    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Alt", Interface = FieldInterface.Text, Sort = 2)]
    public string? Alt { get; set; }
}
```

`MetadataScanner` derives `TranslationMetadata` (`src/Struo.Domain/Metadata/Models/TranslationMetadata.cs`)
from this pair: `ForeignKeyProperty` (the scanner's convention is `{ParentTypeName}Id`, so `FileId`
for `File`), `LocaleProperty` (`Locale`), and `Fields` (the camelCase names of the sidecar's own
`[CmsField]`s — here, `title` and `alt`). The composite unique on `FileId`+`Locale` that guarantees at
most one translation row per parent per locale is not declared on the entity at all: it is derived by
`SqlSugarClientFactory`'s `EntityService` hook from this same `[CmsTranslations]` metadata, via
`TranslationSidecarIndexPolicy` (`src/Struo.Infrastructure/Persistence/TranslationSidecarIndexPolicy.cs`),
which stamps the resolved group name onto the two key columns before `InitTables` reads them. A fork's
own sidecar gets the index for free the same way, purely by carrying `[CmsTranslations(typeof(...))]`
on a new CodeFirst table — nothing to write on the sidecar entity itself. An *existing* sidecar table
that predates this mechanism does not gain the index retroactively; that needs a reviewed migration
under `db/migrations/`, the same as any other constraint added to a live table. `SchemaGuard`
(`src/Struo.Infrastructure/Persistence/SchemaGuard.cs`) verifies the index is actually present at
Development startup, for every sidecar the running configuration has.

A field's `MaxLength` behavior on a translation sidecar follows chapter 5's rules exactly, applied
per-locale by `FieldValueRules.CheckMaxLengthTranslation`; the only difference from a parent-row
field is that the error names the locale: `"Field '{name}' exceeds maximum length {n} for locale
'{locale}'."`

One consequence worth stating plainly: `File` rows are only ever created through the dedicated
upload pipeline (`FileService.UploadAsync`, chapter 11) — file rows are owned by that pipeline, not
by the generic items API. `ItemService.CreateAsync` checks the ordinary `CanWrite` grant *first*
(before `RequireSuperAdminForAdminOnly` and the `File`-collection rejection branch below; chapter 12's
`AdminOnly` section documents this check ordering for every collection) — a caller with no write
grant on `file` at all sees the generic `"Write not permitted."` (`FORBIDDEN`) before ever reaching
the collection-specific rejection below.
Only once that passes does the guard specific to `File` run: `ItemService.CreateAsync`'s
`File`-collection rejection branch rejects a generic `POST /api/items/file` outright with
`400 BAD_USER_INPUT`, quoting the exact message from source: "Files cannot be created through the
generic items API. Upload one with POST /api/files instead." — before it ever reaches translation
validation, `ReadOnly` stripping, or any other generic-create machinery. The same guard rejects a
GraphQL `createFile` mutation identically, since both protocols share `ItemService.CreateAsync`. This
second check is unrelated to `File`'s `fileName`/`contentType`/`size` fields being `ReadOnly`: even a
body that supplied every required column would still be rejected, because the *collection* is
off-limits to generic create, not merely its fields. A collection with a translation sidecar that
*is* created through the generic items API (any collection you define yourself with
`[CmsTranslations]`) has no such restriction — only `File` special-cases creation this way, because
of its dedicated upload pipeline; its per-locale `title`/`alt` are still edited normally afterward
through `PUT /api/items/file/{id}`, the ordinary generic-update path.

## The default-locale rule

On **create**, a translation for the default locale is mandatory. `ItemWriteSideSync.SyncTranslationsAsync`
(`src/Struo.Application/Query/Write/ItemWriteSideSync.cs`) rejects an absent `translations` key, a
non-object value, an empty object, or an object that never mentions the default locale's code — all
with the exact same message, before any parent row insert can be observed by the caller (the parent
insert and the translation sync run inside one transaction, so a rejection here rolls the whole
write back). `File` cannot demonstrate this path (the previous section explained why its own create
route bypasses the generic API entirely), so the two requests below ran against the demo Blog
sample's `Article` collection (chapter 16), opted in temporarily to exercise this rule — the
resulting error shape is identical for any collection you declare your own `[CmsTranslations]` on:

```
$ curl -s -X POST http://localhost:5221/api/items/article \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"status":"draft"}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"A translation for the default locale 'en' is required."}}
```

```
$ curl -s -X POST http://localhost:5221/api/items/article \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"status":"draft","translations":{"zh-TW":{"title":"你好世界"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"A translation for the default locale 'en' is required."}}
```

On **update**, the rule relaxes: an absent or non-object `translations` payload is treated as a
no-op partial update (you can update just the parent fields, or just one non-default locale,
without resupplying every locale), and the default locale is not force-required.

A locale key that is present but not enabled, on either read or write, is rejected the same way
regardless of direction:

```
$ curl -s -X PUT http://localhost:5221/api/items/file/<id> \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"translations":{"fr":{"title":"bonjour"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown or disabled locale 'fr'."}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>?locale=fr-FR"
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Unknown or disabled locale 'fr-FR'."}}
```

A locale code is also charset-validated before the enabled-language check even runs
(`ItemService.ValidateLocale`, `LocaleFormat.IsValid`): it must match `[A-Za-z0-9_-]{1,35}`,
closing off SQL-metacharacter injection through the `?locale=` query parameter.

Two more write-side rules round out the sidecar's validation, both scoped per-locale:

- A field name inside a locale's object that isn't one of the sidecar's own translatable fields is
  rejected: `"Field '{name}' is not a translatable field of '{collection}'."`
- A translatable field marked `Required` (on the *sidecar* entity's own `[CmsField]`) must be
  present and non-empty for every locale supplied, not only the default:
  `"Required translation field '{name}' is missing for locale '{locale}'."`. `File`'s own
  translatable fields (`title`/`alt`) are not `Required`, so this one was verified against the
  sample's `ArticleTranslation.Title` (`Required = true`) instead:

```
$ curl -s -X POST http://localhost:5221/api/items/article \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"status":"draft","translations":{"en":{"body":"no title here"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Required translation field 'title' is missing for locale 'en'."}}
```

```
$ curl -s -X PUT http://localhost:5221/api/items/file/<id> \
    -H "Content-Type: application/json" -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"translations":{"en":{"bogus":"x"}}}'
{"success":false,"error":{"code":"BAD_USER_INPUT","message":"Field 'bogus' is not a translatable field of 'file'."}}
```

## Per-locale SEO

`SeoTranslation` (`src/Struo.Domain/Seo/SeoTranslation.cs`) is an abstract base a translation
sidecar entity can inherit to pick up three ready-made `Group = "SEO"` fields — `SeoTitle`
(`Text`), `SeoMetaDescription` (`Textarea`), `SeoOgImageId` (`Image`) — with no `[SugarColumn]` of
its own (Domain stays package-free; the concrete sidecar's own `[SugarTable]`/columns apply). SEO
is deliberately per-locale-only: there is no parent-side SEO convention to fall back to. The
sample's `ArticleTranslation` (`samples/Struo.Sample.Blog/ArticleTranslation.cs`) is the shipped
example of this inheritance in action.

## How translations appear in REST and GraphQL responses

**REST**: every projected row for a collection with a translation sidecar always carries a
`translations` object, attached by `TranslationOverlay.ApplyAsync`
(`src/Struo.Application/Query/Read/TranslationOverlay.cs`) — independent of `fields=` projection
(covered in chapter 8), since the overlay runs after `ItemProjector`'s own-field selection. Without
`?locale=`, every enabled locale that has a row is included, keyed by locale code:

```
$ curl -s -b cookies.txt http://localhost:5221/api/items/file/<id>
{"success":true,"data":{"id":"...", "fileName":"alpha-report.txt", ...,
  "translations":{"en":{"title":"alpha-report","alt":null},"zh-TW":{"title":"alpha 報告","alt":"Alpha 報告圖示"}}}}
```

With `?locale=`, the map narrows to just that one locale — an empty object `{}` if that parent has
no row for it yet (not a missing key, and not `null`), before any `zh-TW` translation had been
added for this file:

```
$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>?locale=zh-TW"
{"success":true,"data":{"id":"...", "fileName":"alpha-report.txt", ..., "translations":{}}}

$ curl -s -b cookies.txt "http://localhost:5221/api/items/file/<id>?locale=en"
{"success":true,"data":{"id":"...", "fileName":"alpha-report.txt", ..., "translations":{"en":{"title":"alpha-report","alt":null}}}}
```

**GraphQL**: a collection with a sidecar gains a `translations: [Translation!]` field on its object
type, where the shared `Translation` type (`src/Struo.Api/GraphQl/StruoTypeModule.cs`) is
`{ locale: String!, fields: Any! }` — `fields` is the framework's generic JSON scalar (SDL name
`Any`), holding that locale's field map as a raw object rather than a per-collection typed shape:

```
$ curl -s -X POST http://localhost:5221/graphql -H "Content-Type: application/json" \
    -H "X-Struo-CSRF: 1" -b cookies.txt \
    -d '{"query":"{ file(id: \"<id>\") { id fileName translations { locale fields } } }"}'
{"data":{"file":{"id":"<id>","fileName":"alpha-report.txt","translations":[
  {"locale":"en","fields":{"title":"alpha-report","alt":null}},
  {"locale":"zh-TW","fields":{"title":"alpha 報告","alt":"Alpha 報告圖示"}}
]}}}
```

Writing translations over GraphQL uses a dedicated, per-collection **typed** input instead of the
generic map: `CollectionSchemaBuilder` generates an `XTranslationFieldsInput` (one input field per
translatable own-field) and an `XTranslationInput { locale: String!, fields: XTranslationFieldsInput! }`,
and every create/update mutation's input type carries `translations: [XTranslationInput!]` built
from those — chapter 10 covers GraphQL mutations in full.

## Admin locale tabs and completeness indicators

The admin SPA's `ItemForm.vue` renders the tab strip at all only when the collection has
translatable fields (`v-if="fields.translatable.length"` on the `<Tabs>` element) — one tab per row
returned by `GET /api/languages` (via the `languageStore` Pinia store), even when only a single
language is enabled. The small per-tab "dot," however, is gated more narrowly: it only renders when
there is more than one enabled language *and* the collection has translatable fields (`ItemForm.vue`'s
`showDots` computed property) — a single-language install with translatable fields still shows one
(dot-less) tab. Each dot's fill state comes from `hasLocaleContent` (`frontend/src/lib/localeCompleteness.ts`):

```ts
export function hasLocaleContent(fields: FieldMeta[], values: Record<string, unknown>): boolean {
  return fields.some((f) => isNonEmpty(values[f.name]))
}
```

A locale's dot is filled the moment *any one* of its translatable fields holds a non-empty value —
a non-empty trimmed string, a non-empty array, or any other non-null/non-undefined value (numbers
including `0` and booleans including `false` both count as "content"). This is a pure, client-side,
in-memory signal computed from the form model already loaded; it is **not** a validity check and
has no relationship to the `Required`-per-locale rule enforced server-side above — a locale can show
a filled dot while still failing that server check (e.g. a non-required field filled in, the
required one left blank), and vice versa on first load before any edits.

## Next steps

- Chapter 5, [Field Types & Interfaces](05-field-types.md), for the `Translatable` field option and
  storage rules this chapter's sidecar fields draw on.
- Chapter 7, [Relations](07-relations.md), for how a relation's *target* row is resolved — a
  translatable `Image`/`File` field on a sidecar is resolved to its `file` row the same way a
  parent-level relation is.
- Chapter 8, [Query DSL](08-query-dsl.md), for how a translatable field is filtered/sorted at the
  effective query locale.
- Chapter 9, [REST API](09-rest-api.md), for the full request/response envelope and the
  `X-Struo-CSRF` header used on every write example above.
- Chapter 10, [GraphQL API](10-graphql-api.md), for GraphQL mutations' typed translations input in
  full.
