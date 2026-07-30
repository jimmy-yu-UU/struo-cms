# 4. Defining a Collection

This chapter shows how a downstream fork adds its own content type. Everything here is additive: it
touches no framework code, only a new entity class (in your own project) plus, where you deploy
against Postgres, a migration script.

## The metadata-driven model

A **collection** is a plain C# class carrying a `[CmsCollection]` attribute
(`Struo.Domain.Metadata.Attributes.CmsCollectionAttribute`). At startup, `MetadataScanner.Scan`
(`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`)
reflects over every type in the scanned assemblies (see "Where content projects live" below), and for
each `[CmsCollection]`-attributed class builds one `CollectionMetadata` record: its label, icon, group,
default display field, the `AdminOnly`/`Hidden`/`Revisions` flags, whether its entity implements
`ISoftDeletable`, its field list (from each property's `[CmsField]`), its field groups (from
`[CmsFieldGroup]`), its relations (from `[CmsRelation]` + SqlSugar's `[Navigate]`, chapter 7), and its
translation sidecar (from `[CmsTranslations]`, chapter 6) if it has one.

That one `CollectionMetadata` record is the single source every other layer derives from: the database
table (via SqlSugar CodeFirst or a migration script), the REST endpoints and their query-DSL surface
(chapters 8–9), the GraphQL schema (chapter 10), and the admin SPA's navigation, list view and item
form. There is no separate place to declare a REST route, a GraphQL type or an admin screen — the one
attributed C# class is the whole declaration.

The scan is eager and happens once, at DI registration time
(`MetadataServiceCollectionExtensions.AddStruoMetadata`); the result is cached in a singleton
`IMetadataProvider`. Nothing about collection metadata is re-scanned per request.

## Minimal collection

The smallest complete, compilable collection needs: a class inheriting `AuditableEntity`, an overridden
`Id` with a `[SugarColumn(IsPrimaryKey = true)]`, a `[SugarTable]` naming its table, a `[CmsCollection]`
naming the collection, and at least one `[CmsField]`-attributed property. This example lives in a
placeholder `Acme.Content` project — your own fork's content project can be named anything:

```csharp
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Acme.Content;

[SugarTable("announcements")]
[CmsCollection("Announcement", Icon = "megaphone", Group = "Content", DefaultDisplayField = nameof(Title))]
public sealed class Announcement : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public override Guid Id { get; set; }

    [CmsField(Label = "Title", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Title { get; set; } = string.Empty;

    [CmsField(Label = "Body", Interface = FieldInterface.Textarea, Sort = 2)]
    public string Body { get; set; } = string.Empty;

    [CmsField(Label = "Priority", Interface = FieldInterface.Select, Sort = 3)]
    [CmsOptions("low:Low", "medium:Medium", "high:High")]
    public string Priority { get; set; } = "medium";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Published At", Interface = FieldInterface.DateTime, Sort = 4)]
    public DateTime? PublishedAt { get; set; }
}
```

A few things this example relies on, all real, checkable rules:

- `AuditableEntity` (`src/Struo.Domain/Auditing/AuditableEntity.cs`) declares `Id` as `abstract`
  specifically so a forgotten primary key is a **compile error**, not a runtime surprise — the override
  is mandatory, and the `[SugarColumn(IsPrimaryKey = true)]` on that override is what SqlSugar
  recognizes as the table's key. `AuditableEntity` also supplies `CreatedAt`/`CreatedBy`/`UpdatedAt`/
  `UpdatedBy` (stamped automatically); none of these four need a `[CmsField]` — the scanner adds them
  as read-only "system" fields automatically (see chapter 5). `AuditableEntity` separately supplies an
  optimistic-concurrency `Version` column, which is not a system field at all — the scanner ignores any
  property with neither `[CmsField]` nor one of the four audit names, so `Version` reaches API
  responses only because `ItemProjector` emits it directly, outside the field loop, alongside `id`.
- `[SugarTable("announcements")]` names the table explicitly, matching the convention every framework
  and sample entity uses (`src/Struo.Infrastructure/Files/MediaFolder.cs`,
  `samples/Struo.Sample.Blog/Article.cs`, etc.) — a lower-case, plural, snake_case table name.
- A property with neither `[CmsField]` nor the audit-field naming convention is simply ignored by the
  scanner — you can keep ordinary, non-CMS properties on the same entity if you need them.
- Want soft delete or revision history instead of (or in addition to) the fields above? Implement
  `ISoftDeletable` on the class, or set `Revisions = true` on `[CmsCollection]` — chapter 13 covers both
  in full.

## `[CmsCollection]` options

`CmsCollectionAttribute` (`src/Struo.Domain/Metadata/Attributes/CmsCollectionAttribute.cs`) declares
exactly these properties — this is the complete set, not a selection:

| Property | Type | Meaning |
|---|---|---|
| `Label` | `string` (constructor argument, required) | Display label for the collection. |
| `Icon` | `string?` | Icon name shown in the admin sidebar. |
| `Group` | `string?` | Sidebar navigation group this collection is filed under. |
| `DefaultDisplayField` | `string?` | Name of the field used as the item's display title (e.g. in relation pickers, breadcrumbs). Must name a real field — the scanner throws `MetadataException` at startup if it doesn't. |
| `AdminOnly` | `bool` | When `true`, generic-CRUD writes (create/update/delete) require a super-admin regardless of any per-collection RBAC grant — used by the framework's own identity/authorization tables so a delegated write grant can't be turned into self-escalation. Reads still follow ordinary RBAC. |
| `Hidden` | `bool` | Omits the collection from the admin sidebar/nav. It stays fully reachable via REST/GraphQL and direct admin URLs — this is a presentation flag only, not an access rule. |
| `Revisions` | `bool` | Opts the collection into revision history: every successful create/update appends a full post-write snapshot to the framework `revisions` table, and any past revision can be reverted. See chapter 13. |

Two related flags are worth naming even though they aren't attribute properties: **soft delete** is
not a `[CmsCollection]` option at all — the scanner derives `CollectionMetadata.SoftDelete` purely from
whether the entity implements `ISoftDeletable`, no attribute needed (chapter 13). And **RBAC** grants
(who can read/write/delete a collection) are configured separately, per role, in the admin UI or via the
API — a brand-new collection has no grants at all until you add them (chapter 12).

## `[CmsField]` options

`CmsFieldAttribute` (`src/Struo.Domain/Metadata/Attributes/CmsFieldAttribute.cs`) is applied to a
property and declares:

| Property | Type | Meaning |
|---|---|---|
| `Label` | `string?` | Display label; falls back to the CLR property name if omitted. |
| `Interface` | `FieldInterface` | Which storage/editor mapping applies (chapter 5 covers the full set); defaults to `Text`. |
| `Required` | `bool` | Enforced on write (create/update) for non-translatable fields. |
| `Searchable` | `bool` | Included in the collection's free-text search whitelist (chapter 8). |
| `Sortable` | `bool` | Allowed as a query-DSL sort key. |
| `Sort` | `int` | Field ordering in the admin form and (indirectly) the collection list columns. |
| `ReadOnly` | `bool` | Value is returned on read; updates can never move a client-supplied value onto it, and creates strip it back out — with one CLR-type caveat — see chapter 5. |
| `Hidden` | `bool` | Removes the field from schema, GraphQL, item projections, and query filtering/search/sort — see chapter 5. |
| `HelpText` | `string?` | Help text shown under the admin form input. |
| `Translatable` | `bool` | Field lives on the per-locale translation sidecar instead of the parent row (chapter 6). |
| `Group` | `string?` | Name of a `[CmsFieldGroup]` this field belongs to (see below). |
| `MaxLength` | `int` | CMS-layer input-length limit; `0` means unset. Chapter 5 covers its exact defaulting rules. |

`CmsFieldAttribute` also declares a `Display` property, but it is not read anywhere in
`MetadataScanner.BuildField` and has no corresponding property on `FieldMetadata` — it has no observable
effect today; don't rely on it.

## Field groups (`[CmsFieldGroup]`)

`CmsFieldGroupAttribute` (`AllowMultiple = true`, class-level) declares a named section: `Name`
(constructor argument), `Label`, and `Sort`. A field joins a group by setting
`[CmsField(Group = "SameName")]` — the sample's `Article` collection declares two
(`[CmsFieldGroup("Content", ...)]`, `[CmsFieldGroup("SEO", ...)]`): `Article`'s own fields use
`Group = "Content"`, and its translation sidecar's inherited SEO fields
(`src/Struo.Domain/Seo/SeoTranslation.cs`) use `Group = "SEO"`.

Groups are captured in metadata and returned to API/GraphQL callers (`CollectionMetadata.FieldGroups`,
`FieldMetadata.Group`) — but as shipped, the admin SPA's item form (`frontend/src/components/ItemForm.vue`)
does not section the form by group at all: `splitFields.ts` buckets non-system fields only into
`shared` versus `translatable` (each bucket separately `Sort`-ordered), and `ItemForm.vue` renders the
`translatable` bucket inside per-locale tabs, then the `shared` bucket below as a flat list — `Group`
plays no part in either. Declaring groups today mainly documents structure for API consumers rather
than visually partitioning the admin form.

## Options lists (`[CmsOptions]`)

`CmsOptionsAttribute` takes a `params string[]` of options, each entry either `"value:label"` or a bare
`"value"` (label defaults to the value — the sample's `Article.Regions` field has one bare entry,
`"amer"`, alongside labeled ones). `MetadataScanner.ParseOptions` throws `MetadataException` if an
entry's value half is blank.

`[CmsOptions]` is only valid on an option-typed interface — `Select`, `MultiSelect`, `Radio`,
`CheckboxGroup`, or `Tags`; attaching it to any other interface is a startup-time `MetadataException`.
In practice, `Select`/`Radio`/`MultiSelect`/`CheckboxGroup` normally always declare one (there is no
input UI for them without a fixed option list), while `Tags` is usually left without one for genuinely
free-form entry — see `Article.Keywords` in the sample versus `Article.Regions`/`Article.Audiences`,
which do use it.

## Where content projects live and how discovery works

`AddStruoMetadata` (`src/Struo.Infrastructure/DependencyInjection/MetadataServiceCollectionExtensions.cs`)
scans exactly three sources of assemblies:

1. **The framework's own assembly** (`Struo.Infrastructure`) — always appended, so the framework's
   own `[CmsCollection]`-attributed types (`Language`, `File`, `MediaFolder`, `User`, `Role`,
   `Permission`, `UserRole` — seven of the ten framework entity types listed in chapter 1;
   `FileTranslation`, `Revision` and `SiteSettings` are framework tables but not collections) are
   always discovered.
2. **The host assembly** — `Struo.Api` itself (`typeof(Program).Assembly`, passed explicitly from
   `Program.cs`).
3. **Every assembly named in `Struo:ContentAssemblies`** — read from `builder.Configuration` *before*
   `builder.Build()` runs (chapter 3 covers the timing implications of this in full). Each name is
   resolved with `Assembly.Load(new AssemblyName(name))`; **an entry that can't be loaded fails startup**
   with a `MetadataException` naming the entry — it is never silently skipped.

For a named assembly to load, it must actually be resolvable: either referenced by the host project (a
`ProjectReference` in `src/Struo.Api/Struo.Api.csproj`) or otherwise present as a loadable DLL alongside
the host. The shipped host has **no** `ProjectReference` to any content project, and
`Struo:ContentAssemblies` ships as `[]` — which is exactly why a fresh checkout has zero content
collections (chapters 1–2).

To add your own content project: create a class library referencing `Struo.Domain` (for the metadata
attributes/enums) and enough of SqlSugar to declare `[SugarTable]`/`[SugarColumn]`; add a
`ProjectReference` to it from `Struo.Api.csproj`; add its assembly name to `Struo:ContentAssemblies`;
then restart the API (this is a startup-time-only scan — chapter 3). `samples/Struo.Sample.Blog` is
exactly this pattern, already built, as a **detachable demo** — chapter 16 walks through opting it in
with these same two steps, and removing it again cleanly.

## Creating the table: dev `InitTables` vs. production migrations

In **Development**, leave `Database:MigrationsPath` empty (the default) and just run the API. SqlSugar's
CodeFirst step (`DatabaseInitializer.InitializeDevelopmentSchema`, gated on
`app.Environment.IsDevelopment()`) creates any table missing for every scanned entity type — your
content collections, their translation sidecars, and any M2M junction tables
(`EntityTypeCollector.CollectForInitTables` — see `src/Struo.Api/Program.cs`) — and additively adds
missing columns to existing tables. It performs no destructive schema changes and never runs outside
Development.

In **Production**, `InitTables` never runs. Instead, point `Database:MigrationsPath` at a directory of
reviewed `*.sql` scripts (honored only when `Database:DbType` is `PostgreSQL`); the migration runner
applies them at startup, after any dev `InitTables` step and before database seeding. The core schema's
own bootstrap script is `db/migrations/001-core-baseline.sql` — your new collection needs its own
migration script added to that same directory before a production deployment. Chapter 15 covers writing
and applying migrations end-to-end.

## Checklist for adding a collection end-to-end

1. Create (or reuse) a content class library project; reference `Struo.Domain` for the attributes/enums,
   plus enough of SqlSugar (directly, or transitively via `Struo.Infrastructure`) for `[SugarTable]`/
   `[SugarColumn]`.
2. Write the entity: inherit `AuditableEntity`, override `Id` with `[SugarColumn(IsPrimaryKey = true)]`,
   add `[SugarTable("your_table_name")]` and `[CmsCollection("Your Label", ...)]`.
3. Add `[CmsField]` to every property the API/admin form should expose, and `[CmsOptions]` on any
   `Select`/`Radio`/`MultiSelect`/`CheckboxGroup` field.
4. Implement `ISoftDeletable` and/or set `Revisions = true` if the collection needs trash/restore or
   version history (chapter 13).
5. Add relations with `[CmsRelation]` + SqlSugar's `[Navigate]` if the collection references another
   one (chapter 7).
6. Add a `ProjectReference` from `src/Struo.Api/Struo.Api.csproj` to your content project, and add its
   assembly name to `Struo:ContentAssemblies`.
7. Restart the API. In Development, `InitTables` creates the table automatically — confirm the admin
   SPA's sidebar now shows a "Content" navigation group with your collection in it (compare chapter 2's
   "no collections yet" state).
8. Grant RBAC read/write/delete permissions for the collection to the roles that need them (chapter 12)
   — a brand-new collection has no grants yet, so only a super-admin can use it until you do.
9. Before a Production deployment, write a reviewed `*.sql` migration for the new table and add it to
   the directory named by `Database:MigrationsPath` (chapter 15).

## Next steps

- Chapter 5, [Field Types & Interfaces](05-field-types.md), for the full `FieldInterface` reference this
  chapter's `[CmsField]` examples draw on.
- Chapter 6, [Internationalization](06-internationalization.md), for `Translatable` fields and
  `[CmsTranslations]`.
- Chapter 7, [Relations](07-relations.md), for `[CmsRelation]`.
- Chapter 12, [Authentication, SSO & RBAC](12-auth-and-rbac.md), to grant a new collection's permissions.
- Chapter 13, [Revisions & Soft Delete](13-revisions-and-soft-delete.md), for `Revisions` and
  `ISoftDeletable` in full.
- Chapter 15, [Deployment, Operations & Testing](15-deployment-operations-testing.md), for writing and
  applying production migrations.
- Chapter 16, [Sample Walkthrough](16-sample-walkthrough.md), to see this whole checklist already done
  for the Blog sample.
