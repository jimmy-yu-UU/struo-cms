# 16. Sample Walkthrough

This chapter covers `samples/Struo.Sample.Blog/`, a small blog-shaped demo shipped inside this
repository. It is not core capability — it exists purely to show what chapter 4's checklist produces
once every step has actually been carried out: a full collection with fields and groups, a translation
sidecar, a many-to-many relation through a junction table, a self-referencing tree, and a Repeater
sub-field type. This chapter opts it in, tours each file, runs its dedicated end-to-end suite, and then
removes it again completely — both halves were followed literally against this checkout while writing
this chapter.

## What the sample is for

`samples/Struo.Sample.Blog/Struo.Sample.Blog.csproj` is a plain class library referencing
`Struo.Domain` (for the `[Cms*]` attributes) and `Struo.Infrastructure` (for SqlSugar, transitively) —
one of the two variants chapter 4's checklist allows for a content project's SqlSugar dependency. The
sample's `.csproj` actually carries **both** variants at once: that transitive path through
`Struo.Infrastructure`, plus its own direct `SqlSugarCore` `PackageReference` — the second variant the
checklist allows. It declares three collections with their own admin sidebar entry —
`Article`, `Tag`, `Category` — plus `ArticleTranslation` (`Article`'s translation sidecar, not a
collection of its own), the `Article`↔`Tag` many-to-many junction `ArticleTag` (itself a
`[CmsCollection]`, `Hidden = true` so it carries no sidebar entry of its own — see below), and
`FaqItem`, a plain POCO used as a Repeater sub-field type.

Nothing under `src/Struo.*` references it. The shipped `src/Struo.Api/Struo.Api.csproj` has no
`ProjectReference` to it, and the shipped `Struo:ContentAssemblies` is `[]` — confirmed in chapter 2's
"no collections yet" walkthrough. The sample is wired into exactly one place in the shipped tree:
`tests/Struo.Tests`, whose backend test suite uses these same collections as concrete fixtures for
exercising generic framework behavior (RBAC, revisions, soft delete, relations, i18n, the query DSL).
That coupling is why removing the sample is more involved than deleting one directory — the "Removing
the sample completely" section below covers it in full, verified end-to-end against this checkout.

## Opting it in

Two edits, both required, in this order:

**1. Add the `ProjectReference`** to `src/Struo.Api/Struo.Api.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\Struo.Application\Struo.Application.csproj" />
  <ProjectReference Include="..\Struo.Infrastructure\Struo.Infrastructure.csproj" />
  <ProjectReference Include="..\..\samples\Struo.Sample.Blog\Struo.Sample.Blog.csproj" />
</ItemGroup>
```

**2. Add the assembly name to `Struo:ContentAssemblies`.** In `appsettings.Development.json` (copied
from `appsettings.Development.json.example` per chapter 2), this line already exists commented out —
uncomment it:

```json
"Struo": {
  "ContentAssemblies": [ "Struo.Sample.Blog" ],
  ...
}
```

(A non-Development environment would add the same array to `appsettings.json` or override it via
`Struo__ContentAssemblies__0=Struo.Sample.Blog`.)

Both edits are required together: the assembly must be *resolvable* (the `ProjectReference`) and it
must be *named* (`ContentAssemblies`) — chapter 4 covers why. `Struo:ContentAssemblies` is read once,
before the host builds (chapter 3), so the change only takes effect on the **next** restart; a running
instance must be stopped and started again, not just have its configuration file edited underneath it.

Restarting after both edits, CodeFirst then creates the sample's tables
(`articles`, `article_translations`, `tags`, `article_tags`, `categories`) the same way it creates any
other content collection's tables, in any environment. Verified live against this checkout: after making
both edits and restarting, `GET /api/schema` (as the bootstrap admin) lists `article`, `category` and
`tag` alongside the seven framework collections (chapter 4), where a moment before it listed only the
framework ones.

While the sample is opted in this way, one shipped backend test intentionally goes red:
`tests/Struo.Tests/Template/TemplateInvariantsTests.cs`'s
`Host_project_has_no_project_reference_into_samples` reads `Struo.Api.csproj` directly and asserts no
`ProjectReference` line mentions `samples` — that is precisely what step 1 above just added, so
`dotnet test` will report this one failure until the revert in "Removing the sample completely" step 1
puts the csproj back.

## Guided tour

Each file below was read directly against the cited source.

### `Article.cs` — a full collection

```csharp
[SugarTable("articles")]
[SugarIndex("ix_articles_categoryid", nameof(CategoryId), OrderByType.Asc)]
[CmsCollection("Article", Icon = "article", Group = "Content", DefaultDisplayField = nameof(Status), Revisions = true)]
[CmsFieldGroup("Content", Label = "Content", Sort = 1)]
[CmsFieldGroup("SEO", Label = "SEO", Sort = 2)]
public sealed class Article : AuditableEntity, ISoftDeletable
```

This is the sample's centerpiece and the one chapter 4's field-groups section points back to. It
implements `ISoftDeletable` (`DeletedAt`/`DeletedBy`) and sets `Revisions = true`, so it has both trash
and version history (chapter 13). It declares two field groups, `Content` and `SEO` — its own fields all
use `Group = "Content"`; the `SEO` group is used by fields inherited through its translation sidecar
(see below). Beyond `Status` (a required `Select` with `draft`/`published` options, and the collection's
`DefaultDisplayField`), it walks nearly every field interface in one place: `PublishedAt` (`DateTime`),
`HeroImageId` (`Image`), `Regions` (`MultiSelect`, with one option — `"amer"` — left unlabeled so it
falls back to the raw value), `Audiences` (`CheckboxGroup`), `Keywords` (`Tags`, left without a fixed
`[CmsOptions]` list — chapter 4 notes this is the usual pattern for a genuinely free-form `Tags` field),
`Attributes` (`Json`), `Meta` (`KeyValue`), `Gallery` (a multi-file
`Files` field), `Faqs` (`Repeater`, of `FaqItem` — see below), and `InternalNote`, a `Hidden` text field
excluded from schema, GraphQL and item projections. It also declares two relations: `Category` (a
many-to-one `Dropdown`, `OnDelete = SetNull`) and `Tags` (a many-to-many `TagSelect` through the
`ArticleTag` junction, via `[Navigate(typeof(ArticleTag), ...)]`, ordered by
`[CmsRelation(SortField = nameof(ArticleTag.Sort))]`). Title and body are **not** here — they live on
the translation sidecar, next.

### `ArticleTranslation.cs` — the translation sidecar

```csharp
[SugarTable("article_translations")]
public sealed class ArticleTranslation : Struo.Domain.Seo.SeoTranslation
```

Referenced from `Article` via `[CmsTranslations(typeof(ArticleTranslation))]` (chapter 6). It carries
the per-locale `ArticleId`/`Locale` pair under a composite `UNIQUE` constraint (one translation row per
locale per article), the required, searchable `Title` (`Group = "Content"`), a `RichText` `Body`, and a
second `Hidden` field, `InternalSlug`, mirroring `Article.InternalNote`. By inheriting `SeoTranslation`
it also picks up `SeoTitle`/`SeoMetaDescription`/`SeoOgImageId` for free, filed under the `SEO` group
declared on `Article` — this is exactly the mechanism chapter 4's field-groups section points to.

### `Tag.cs` + `ArticleTag.cs` — many-to-many through a junction

`Tag` (`[SugarTable("tags")]`) is the simplest real collection in the sample: just a required,
searchable `Name`. `ArticleTag` (`[SugarTable("article_tags")]`) is the join table `Article.Tags`'s
`[Navigate(typeof(ArticleTag), ...)]` relation walks through — but unlike a bare junction, it **is**
itself a `[CmsCollection]` (`Hidden = true`, so it never appears in the admin sidebar) with payload
beyond its two foreign keys: a `Note` text field, and a `Sort` number field that doubles as
`Article.Tags`' `SortField`. It exists to demonstrate the junction-payload feature (chapter 7) end to
end against a real, running collection — `Note` is data that belongs to the article↔tag *link* itself
(why this tag is attached to this article), not to either endpoint:

```csharp
// samples/Struo.Sample.Blog/ArticleTag.cs
using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

// The article<->tag M2M junction, promoted to a [CmsCollection] so the shipped sample demonstrates
// the junction-payload feature end to end: Note is extra data carried BY the link itself (not by
// either endpoint), and Sort lets an admin order an article's tags (wired via
// Article.Tags' [CmsRelation(SortField = nameof(Sort))]). Hidden = true keeps it out of the admin
// sidebar — it is still reachable through /api/schema, the RBAC matrix and GraphQL, same as any
// other collection; only the sidebar treats it specially. Both foreign keys must stay declared as
// writable [CmsField]s (Interface = FieldInterface.Uuid): MetadataScanner.ValidateJunctionCollections
// fails fast at startup on any junction collection whose FKs aren't writable, because the generic
// CRUD API would otherwise create rows with empty keys. A fork that deletes this sample (per
// AGENTS.md's core/sample boundary) loses nothing else — no core code references this type. An
// existing dev database picks up the two new columns (note, sort) only through Development's
// AutoSyncSchema or a fork-authored migration; this table is not part of db/migrations.
[SugarTable("article_tags")]
// CodeFirst-declared secondary indexes on both M2M junction FKs, both directions (RelationExpander
// expansion + sync-on-write), created by InitTables in dev; a downstream fork that keeps this
// entity should add the equivalent indexes to its own migrations.
[SugarIndex("ix_article_tags_articleid", nameof(ArticleId), OrderByType.Asc)]
[SugarIndex("ix_article_tags_tagid", nameof(TagId), OrderByType.Asc)]
[CmsCollection("Article tag", Icon = "link", Group = "Content", Hidden = true)]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Article", Interface = FieldInterface.Uuid, Required = true, Sort = 1)]
    public Guid ArticleId { get; set; }

    [CmsField(Label = "Tag", Interface = FieldInterface.Uuid, Required = true, Sort = 2)]
    public Guid TagId { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Note", Interface = FieldInterface.Text, MaxLength = 200, Sort = 3)]
    public string? Note { get; set; }

    [CmsField(Label = "Sort", Interface = FieldInterface.Number, Sort = 4)]
    public int Sort { get; set; }
}
```

`Required = true` on `ArticleId`/`TagId` here is decorative, not the mechanism that actually guards
against an empty junction key — chapter 7's fail-fast validation (declaring both FKs as writable
`[CmsField]`s) is what does that; `FieldValueRules.IsMissing`
(`src/Struo.Application/Query/Write/FieldValueRules.cs`), the check `Required` runs against, only
rejects a `null` value or a blank *string*, and a `Guid` FK deserializes to a real `Guid` value, never a
string, so `Required` never actually catches an omitted or empty one here. If you run this sample
against a database created before this feature existed, the two new columns (`note`,
`sort`) need to actually reach the table before `article_tags` rows can carry them — either
`Database:AutoSyncSchema=true` in Development (chapter 15) or a migration you author yourself, since
`db/migrations` is core-only and never ships sample-collection schema (this chapter's own
sample-removal checklist, below, notes the same boundary for the sample's tables generally).

Because `ArticleTag` is a `[CmsCollection]` in its own right, it is also fully reachable through the
generic CRUD/GraphQL surface on the junction table itself (`articleTag`, not just through
`Article.Tags`) — and it declares no unique index on `(ArticleId, TagId)`, so a direct write through
that surface can create a duplicate pair; the next save of the owning article repairs it (the
lowest-PK row is kept, the rest deleted, and a warning is logged — `ManyToManySync`, chapter 7).

### `Category.cs` — many-to-one with a self-referencing tree

```csharp
[SugarTable("categories")]
[SugarIndex("ix_categories_parentid", nameof(ParentId), OrderByType.Asc)]
[CmsCollection("Category", Icon = "folder", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Category : AuditableEntity, ISoftDeletable
```

`Category` is soft-deletable and self-referencing: `ParentId` plus a `Parent` relation
(`TreeSelect`, `OnDelete = SetNull`) and a reverse `Children` relation (`RelatedList`) walking the same
FK the other direction. It additionally declares `Articles`, a `RelatedList` walking
`Article.CategoryId` the other way — the many-to-one relation `Article` declares against `Category`,
seen from `Category`'s side.

### `FaqItem.cs` — a minimal, non-collection type

```csharp
public sealed class FaqItem
{
    [CmsField(Label = "Question", Interface = FieldInterface.Text, Required = true)]
    public string Question { get; set; } = "";

    [CmsField(Label = "Answer", Interface = FieldInterface.Textarea)]
    public string Answer { get; set; } = "";

    [CmsField(Label = "Category", Interface = FieldInterface.Select)]
    [CmsOptions("general:General", "billing:Billing")]
    public string? Category { get; set; }
}
```

No `[SugarTable]`, no `[CmsCollection]` — `FaqItem` is a plain POCO whose `[CmsField]`-attributed
properties are read only as the sub-field shape of `Article.Faqs`'s `Repeater` interface. It is the
smallest possible demonstration of a Repeater's item type: a required `Text`, a plain `Textarea`, and an
optional `Select` with two options.

## Running the sample E2E suite

`frontend/e2e/sample/` holds eight spec files (`collections`, `items`, `relations`, `revisions`,
`trash`, `conflict`, `unsaved-guard`, `not-found`) exercising the sample's collections end-to-end:
browsing and CRUD on `article`, relation editing and its `RelatedList` side, the revision-history
drawer and revert, soft-delete/restore/purge, the optimistic-concurrency conflict banner, and the
unsaved-changes navigation guard. `frontend/playwright.config.ts` wires them up as the `sample` project,
run with:

```bash
pnpm e2e:sample
```

This needs everything `pnpm e2e` (chapter 15) needs — a running API, a reachable database, and the
seeded bootstrap admin, with no login-rate-limit override needed for a default checkout (the per-account
throttle only counts failed logins, and no spec performs one) — **plus** the Blog sample opted into
`Struo:ContentAssemblies` as above, since every one of these specs
depends on the `article`/`category`/`tag` collections existing. `frontend/e2e/README.md` documents the
full prerequisite list and per-spec seed-data notes; nothing here changes them.

Confirmed live against this checkout with the sample opted in: `pnpm e2e:sample` discovers all eight
spec files (13 tests) and drives them against the live API — including full create/edit/delete on
`article`, relation editing with `RelatedList` navigation, and the stale-version conflict banner — over
a real logged-in session against the collections toured above. `pnpm e2e:all` runs this project and the
core project (chapter 15) together in one invocation.

## Removing the sample completely

Deleting `samples/Struo.Sample.Blog/` is not, by itself, enough to leave a clean build: this
repository's own backend test suite uses the sample's collections as fixtures far beyond the demo
directory itself, both by importing the sample's types directly and by exercising `article`/`category`/
`tag` over REST/GraphQL without importing anything. The full checklist below was executed end-to-end
against this checkout; every step here was necessary to reach a green build and a green test run — none
is optional.

1. **Revert the two opt-in edits**, if made: remove the `<ProjectReference>` to
   `Struo.Sample.Blog.csproj` from `src/Struo.Api/Struo.Api.csproj`, and remove (or re-comment) the
   `"Struo.Sample.Blog"` entry from `Struo:ContentAssemblies` in your own `appsettings.Development.json`.
   Also remove the matching, still-commented block from the tracked
   `src/Struo.Api/appsettings.Development.json.example` (the `// Uncomment to enable the Blog sample …`
   comment and the commented `"ContentAssemblies": [ "Struo.Sample.Blog" ]` line) — otherwise the
   shipped example keeps pointing every future reader at a sample that no longer exists.
2. **Delete `samples/Struo.Sample.Blog/`** entirely.
3. **Remove its entry from `StruoCMS.slnx`** — the whole `<Folder Name="/samples/">` block.
4. **Remove the `<ProjectReference>`** to `Struo.Sample.Blog.csproj` from
   `tests/Struo.Tests/Struo.Tests.csproj`.
5. **Delete `tests/Struo.Tests/Support/ContentAssemblyEnvBootstrap.cs`.** This is the file that
   actually wires the sample into every test host in the suite: a `[ModuleInitializer]` that sets the
   environment variable `Struo__ContentAssemblies__0=Struo.Sample.Blog` process-wide, before any test
   runs — a workaround for `Struo:ContentAssemblies` being read before `WebApplicationFactory` can
   inject its own configuration overrides (its own doc comment explains this in full). Without this
   file, no `ApiFactory`-based test host has the sample's collections at all.
6. **Delete every test file that imports the sample's types directly** (`using Struo.Sample.Blog;`).
   At the time of writing this is 26 files: 17 under `tests/Struo.Tests/Query/`, 3 under `Metadata/`,
   3 under `Persistence/`, 2 under `Revisions/`, 1 under `Health/`. Find the current set with:

   ```bash
   grep -rl "using Struo.Sample.Blog;" tests/Struo.Tests
   ```

   `dotnet build` succeeds once these are gone (nothing under `src/Struo.*` ever referenced the sample),
   but `dotnet test` does not yet — the next two steps are why.
7. **Fix `tests/Struo.Tests/Metadata/ConventionMetadataDiscoveryTests.cs`.** Two of its four tests
   configure `"Struo.Sample.Blog"` as a `Struo:ContentAssemblies` entry **by name** (a plain string, not
   an import) and assert it loads successfully — delete
   `Configured_content_assembly_is_scanned` and `Registers_entity_type_collector`, or rewrite them
   against an assembly your fork actually ships. The other two tests (the empty-config and
   unloadable-assembly cases) don't reference the sample and are unaffected.
8. **Delete every test file that exercises the `article`/`category`/`tag` collections over REST or
   GraphQL without ever importing the sample's namespace.** These rely entirely on step 5's module
   initializer, so nothing catches them at compile time — `dotnet test` is what finds them, one
   `HTTP 404`/`KeyNotFoundException` failure per collection-shaped test, spread across `Api/`, `Query/`,
   `GraphQl/`, `Localization/` and `Identity/`. Nothing marks these files as sample-coupled ahead of
   time and the set will drift as the suite grows, so there is no static command to point at instead of
   running it: run `dotnet test` after steps 1–7, delete whichever file each failure is in, and repeat
   until it's green — that loop, not a fixed list, is what this checklist was actually verified with.
   While you're in these files, also tidy the handful of comments left pointing at what you just
   deleted — `tests/Struo.Tests/Support/ApiFactory.cs` and `tests/Struo.Tests/Api/CorsAndCookieTests.cs`
   each have a comment citing the now-gone `ContentAssemblyEnvBootstrap.cs`, and
   `tests/Struo.Tests/GraphQl/FakeMetadataFixtures.cs` and `GraphQl/GraphQlExecutionTests.cs` each cite a
   `samples/Struo.Sample.Blog/` path in a comment. None of these break the build, but they'll be stale.
9. **On the frontend:** delete `frontend/e2e/sample/` (8 spec files); remove the `"sample"` project
   entry from `frontend/playwright.config.ts` (and its `core` project's now-unnecessary
   `testIgnore: '**/e2e/sample/**'`, since there's nothing left to ignore); remove the `"e2e:sample"`
   and `"e2e:all"` scripts from `frontend/package.json`, leaving `"e2e"` as the only end-to-end script;
   and prune the sample-suite material from `frontend/e2e/README.md` — the `sample` bullet in its
   opening suite list, the `pnpm e2e:sample` prerequisite bullet (Blog sample opt-in and `E2E_STAMP`),
   and the chapter-16 pointer in its "Further reading" section.
10. **Drop the sample's tables** from any database that has run it — CodeFirst created them, and
    nothing drops them automatically: `articles`, `article_translations`, `tags`, `article_tags`,
    `categories`. They were never part of any tracked migration under `db/migrations/` (chapter 15), so
    no migration needs writing to remove them — a direct
    `DROP TABLE IF EXISTS article_tags, article_translations, articles, categories, tags CASCADE;`
    against your development database is enough.

**Verification, run in this order:**

```bash
dotnet build
dotnet test
cd frontend && pnpm test && pnpm build && pnpm e2e
```

Confirmed against this checkout, after every step above: `dotnet build` succeeds with no warnings, and
`dotnet test`, `pnpm test`, and `pnpm build` (`vue-tsc -b && vite build`) all pass completely — every
remaining test in both suites, with none of the failures the deleted files used to cause. `pnpm e2e`
needs a running API and database exactly as chapter 15 describes — that requirement, and the core
suite's behavior, are unaffected by removing the sample.

## Next steps

- Chapter 4, [Defining a Collection](04-defining-a-collection.md), for the checklist this sample is a
  worked example of.
- Chapter 13, [Revisions & Soft Delete](13-revisions-and-soft-delete.md), for `Article`'s `Revisions`
  flag and both collections' `ISoftDeletable` implementation in full.
- Chapter 15, [Deployment, Operations & Testing](15-deployment-operations-testing.md), for the `core`
  E2E project, the backend/frontend test layers, and what CI does and doesn't run.
