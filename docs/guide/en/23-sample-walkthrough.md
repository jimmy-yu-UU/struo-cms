# 23. Sample Project Walkthrough

This chapter walks through `samples/Struo.Sample.Blog` — a working, runnable example of every
mechanism [Chapter 5: Defining Collections](05-collections.md) through
[Chapter 9: Revisions and Soft Delete](09-revisions-and-trash.md) covers. What follows: what it
demonstrates, how to opt it in, what to look at file by file, and how to remove it completely.

## What the sample demonstrates

`samples/Struo.Sample.Blog/` is a small blog example that lives in the repository alongside the
template itself. It demonstrates a full collection, a translation sidecar, a many-to-many
junction that carries its own payload, a self-referencing tree collection, and a `Repeater` child
type — one instance of each:

- `Article`: a full collection, covering a range of field interfaces, two relations, revisions and
  soft delete, and two field groups.
- `ArticleTranslation`: `Article`'s translation sidecar.
- `Tag` and `ArticleTag`: a many-to-many relation whose junction itself carries a payload.
- `Category`: a self-referencing tree collection.
- `FaqItem`: a plain POCO used as `Repeater`'s child type.

`Struo.Sample.Blog.csproj` is an ordinary class library. It references `Struo.Domain` (for the
`[CmsCollection]`/`[CmsField]` attributes) and takes both paths to SqlSugar at once: it
references the `SqlSugarCore` package directly, and it also references `Struo.Infrastructure`
(which brings SqlSugar in transitively too) — [Chapter 5](05-collections.md) covers the direct
reference as the minimum for a library that only declares entities. The sample's own migration
derives from `Struo.Infrastructure`'s `StruoMigration`, so it needs the second reference as well.

The only project that references it is its own test project, `samples/Struo.Sample.Blog.Tests`.
The core tests in `tests/` don't touch it: they run on the test-only fixtures in
`tests/Struo.Tests.Fixtures` ([Chapter 22](22-testing.md)).
`src/Struo.Api/Struo.Api.csproj` carries no `ProjectReference` to it, and
`Struo:ContentAssemblies` defaults to an empty array.

## Opting the sample in

Opting the sample in needs two edits, and both are required. First, add a project reference in
`src/Struo.Api/Struo.Api.csproj`:

```xml
<ProjectReference Include="..\..\samples\Struo.Sample.Blog\Struo.Sample.Blog.csproj" />
```

Second, uncomment the `"ContentAssemblies"` line in your own
`src/Struo.Api/appsettings.Development.json` — the gitignored copy of the tracked example that
[Chapter 3: Getting Started](03-getting-started.md) has you make. The block to look for is the one
the example carries (outside Development, put the array in `appsettings.json` instead, or set
`Struo__ContentAssemblies__0`):

```json
  // Uncomment to enable the Blog sample (see docs/guide/en/23-sample-walkthrough.md).
  // The Struo.Sample.Blog project reference must be added to Struo.Api.csproj as well.
  "Struo": {
    // "ContentAssemblies": [ "Struo.Sample.Blog" ],
```

`Struo:ContentAssemblies` is read at service-registration time, so changing only the settings file
has no effect on its own — the process has to be restarted before the edit takes effect.

If you only want to take a look, reverting both edits afterward is enough; the sample's code stays
in place and affects nothing else. To remove it from a fork entirely, see "Removing the sample
completely" below.

The sample carries its own migration, `Migrations/202610080100_CreateBlogSchema.cs`. With both
edits done, that migration is not applied yet; with `Database:MigrateOnStartup` set to `false`, the
restarted API refuses to start and exits with code `1`:

```
[18:00:42 FTL] StruoCMS host terminated unexpectedly
System.InvalidOperationException: 1 migration(s) pending, the lowest is 202610080100; run the 'migrate' command, or set Database:MigrateOnStartup to true.
…
```

Run `migrate` once to create the five tables `articles`, `article_translations`, `tags`,
`article_tags` and `categories`:

```
$ dotnet run --project src/Struo.Api -- migrate
…
Applied 202610080100  CreateBlogSchema
```

With `MigrateOnStartup` set to `true`, the API applies it itself on start. Once the API is running
again, `GET /api/schema` lists eleven collections: the
sample's four (`article`, `articleTag`, `category`, `tag`) alongside the seven framework
collections; `ArticleTranslation` is a sidecar, not a collection of its own. `articleTag` is
`Hidden` in the admin sidebar, but `/api/schema` still lists it.

While the sample is opted in, one `dotnet test` test stays red:
`TemplateInvariantsTests.Host_project_has_no_project_reference_into_samples` reads
`Struo.Api.csproj` directly and asserts that it carries no `ProjectReference` into `samples` —
exactly the line the first step above just added. Getting back to green is step 1 of the removal
list below.

## File by file

Seven `.cs` files; the lead collection comes first and the sample's migration last.

### `Article.cs`

`Article` (`AuditableEntity, ISoftDeletable`) is the sample's lead collection:

```csharp
[SugarTable("articles")]
[SugarIndex("ix_articles_categoryid", nameof(CategoryId), OrderByType.Asc)]
[CmsCollection("Article", Icon = "article", Group = "Content", DefaultDisplayField = nameof(Status), Revisions = true)]
[CmsFieldGroup("Content", Label = "Content", Sort = 1)]
[CmsFieldGroup("SEO", Label = "SEO", Sort = 2)]
public sealed class Article : AuditableEntity, ISoftDeletable
```

It implements `ISoftDeletable` and sets `Revisions` to `true`, so it carries both the trash and
revisions at once — see [Chapter 9](09-revisions-and-trash.md).

Its own fields cover most of the interfaces in Chapter 6's overview: `Select` (`Status`),
`DateTime` (`PublishedAt`), `Image` (`HeroImageId`), `MultiSelect` (`Regions`), `CheckboxGroup`
(`Audiences`), `Tags` (`Keywords`), `Json` (`Attributes`), `KeyValue` (`Meta`), `Files`
(`Gallery`), `Repeater` (`Faqs`, whose child type is `FaqItem` below), and `InternalNote`, a `Text`
field marked `Hidden`.

What each interface does is in [Chapter 6: Field Types and Editors](06-field-types.md).

`Status` is a `draft`/`published` `Select` that defaults to `draft` in code and is also the
collection's `DefaultDisplayField`, but it doesn't declare `Required`. The fields that are actually
required in the sample are `ArticleTranslation.Title`, `Tag.Name`, `Category.Name`,
`FaqItem.Question`, and `ArticleTag`'s two foreign keys.

Two relations: `Category` (a many-to-one `Dropdown`, `OnDelete = SetNull`) and `Tags` (a
many-to-many `TagSelect` through the `ArticleTag` junction, sorted with
`[CmsRelation(SortField = nameof(ArticleTag.Sort))]`, letting the form edit each tag's `Note` and
order in place); how both relation interfaces work is in
[Chapter 8: Relations](08-relations.md). The title and body aren't on `Article` itself — they live
on the translation sidecar below.

### `ArticleTranslation.cs`

```csharp
[SugarTable("article_translations")]
public sealed class ArticleTranslation : Struo.Domain.Seo.SeoTranslation
```

`ArticleTranslation` is `Article`'s translation sidecar, pointed to by the
`[CmsTranslations(typeof(ArticleTranslation))]` on `Article`; the `ArticleId`/`Locale` composite
unique index needs no declaration of its own. Its required, searchable `Title` sits in the
`Content` group, `Body` is `RichText`, and there's also a `Hidden` `InternalSlug`. How a
translation sidecar works is in [Chapter 7: Multilingual Content](07-i18n.md).

Inheriting `SeoTranslation` gets it `SeoTitle`/`SeoMetaDescription`/`SeoOgImageId` for free —
these three fields sit under the `SEO` group `Article` declares.

### `Tag.cs` and `ArticleTag.cs`

`Tag` (`[SugarTable("tags")]`) is the simplest collection in the sample: one required, searchable
`Name`. `Article.Tags` reaches it through a `[Navigate(typeof(ArticleTag), ...)]` junction,
`ArticleTag` (`[SugarTable("article_tags")]`), which itself carries
`[CmsCollection(..., Hidden = true)]` — making it a readable, writable junction collection:
besides its two foreign keys, it carries two more fields, a `Note` text field — this link's own
payload — and `Sort`, which doubles as `Article.Tags`'s sort field.

The full mechanics of a junction collection are in [Chapter 8](08-relations.md).

`ArticleTag`'s two foreign keys (`ArticleId`/`TagId`) declare `Required = true`, but that rule
doesn't apply when payload is written through the `Article.Tags` relation array; the difference
between the two write paths is in [Chapter 8](08-relations.md).

`ArticleTag` carries no unique index blocking a duplicate `(ArticleId, TagId)` pair — writing to
`articleTag` directly can produce a duplicate pairing; the next time the owning `Article` is saved
it's cleaned up (the row with the smallest primary key stays, the rest are deleted, and a warning
is logged) — see [Chapter 8](08-relations.md).

### `Category.cs`

```csharp
[SugarTable("categories")]
[SugarIndex("ix_categories_parentid", nameof(ParentId), OrderByType.Asc)]
[CmsCollection("Category", Icon = "folder", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class Category : AuditableEntity, ISoftDeletable
```

`Category` (`AuditableEntity, ISoftDeletable`) self-references into a tree: `ParentId` paired with
a `TreeSelect` `Parent` relation (`OnDelete = SetNull`), and `Children` (a `RelatedList`) walking
the same foreign key back the other way; plus `Articles`, a `RelatedList` walking
`Article.CategoryId` back. Its own fields are just a required, searchable `Name`. How all three
relation interfaces behave is in [Chapter 8](08-relations.md).

### `FaqItem.cs`

`FaqItem` is a plain POCO — no `[SugarTable]`, no `[CmsCollection]`. Its `[CmsField]`s only shape
it as the child type of the `Article.Faqs` `Repeater` field: one required `Text` (`Question`), one
`Textarea` (`Answer`), and a two-option `Select` (`Category`).

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

### `Migrations/202610080100_CreateBlogSchema.cs`

`CreateBlogSchema` derives from `StruoMigration` and creates the sample's five tables in one
migration: every column, `ArticleTranslation`'s unique `(articleid, locale)` index, and four plain
indexes on `articles`, `categories` and `article_tags`. `Down()` drops the five tables in reverse
order.

Its `Up()` matches what `make:migration --entity` generates for those five types, so it serves as a
complete example of that command; the command and the authoring rules are in the "Writing a
migration" section of [Chapter 21: Schema Management and Upgrades](21-schema-and-upgrades.md).

## Running the sample's end-to-end tests

`frontend/e2e/sample/` has 8 spec files (`collections`, `items`, `relations`, `revisions`,
`trash`, `conflict`, `unsaved-guard`, `not-found`) and 15 tests; `frontend/playwright.config.ts`
wires them into the `sample` Playwright project, run with `pnpm e2e:sample`.

Running this suite needs everything `pnpm e2e` itself needs — a running API, a reachable
database, a seeded bootstrap administrator; see [Chapter 22: Testing and CI](22-testing.md) —
plus the sample added to `Struo:ContentAssemblies`. The full prerequisites and each spec's
seed-data notes are in `frontend/e2e/README.md`; `relations.spec.ts` in particular needs one
`category` row and one `tag` row already on file — the sample itself carries no seed data.

## Removing the sample completely

The sample and its tests sit side by side, so removing it is four steps. `tests/` needs no change:
the core tests run on `tests/Struo.Tests.Fixtures`, not on the sample.

1. If you opted the sample in, revert both edits: remove the `ProjectReference` to
   `Struo.Sample.Blog.csproj` from `Struo.Api.csproj`, and re-comment (or delete) the
   `"ContentAssemblies"` line in your `appsettings.Development.json`. Then delete that line and the
   two comment lines above `"Struo"` from the tracked `appsettings.Development.json.example` too;
   otherwise the tracked example keeps pointing at a sample that doesn't exist.

2. Delete the whole `samples/` directory, which holds the sample and its test project
   `Struo.Sample.Blog.Tests`, and remove the `<Folder Name="/samples/">` block from `StruoCMS.slnx`.

3. Delete `frontend/e2e/sample/`. `frontend/playwright.config.ts` adds the `sample` project only
   while that directory exists, so the config needs no edit. Remove the `"e2e:sample"` and
   `"e2e:all"` scripts from `frontend/package.json`, which would otherwise name a project that is
   gone, and trim the sample's section out of `frontend/e2e/README.md`.

4. Clean up the five tables in any database the sample has run against, and the sample migration's
   row in the version table. The tables the sample migration created don't go away with the code;
   left in place, the row shows up as `Orphaned` in `migrate:status` — it doesn't block startup,
   but it stays there:

   ```
   DROP TABLE IF EXISTS article_tags, article_translations, articles, categories, tags;
   DELETE FROM struo_schema_versions WHERE version = 202610080100;
   ```

   Replace `struo_` with your `Database:TablePrefix`. The sample's tables have no foreign keys
   between them, so no `CASCADE` is needed. Both statements were run on PostgreSQL, SQL Server,
   MySQL and MariaDB; SQLite's `DROP TABLE` takes one table at a time, so split it into five.

After step 4, `migrate:status` lists only the two core migrations:

```
$ dotnet run --project src/Struo.Api -- migrate:status
202610080000   Applied   2026-10-12 10:34:10Z  CreateCoreSchema
202610080001   Applied   2026-10-12 10:34:10Z  SeedCoreData
2 applied, 0 pending, 0 orphaned
```

After removal, run these three commands, in order, to confirm it's clean:

```
dotnet build
dotnet test
cd frontend && pnpm test && pnpm build && pnpm e2e
```

## What's next

That's the sample walkthrough. To build a collection of your own, go back to
[Chapter 5: Defining Collections](05-collections.md); if you're just getting started with the
project, begin at [Chapter 3: Getting Started](03-getting-started.md). If your database was built by
v0.8.x, see [Chapter 24: Joining a Database Built by v0.8.x](24-upgrading-from-0-8.md).
