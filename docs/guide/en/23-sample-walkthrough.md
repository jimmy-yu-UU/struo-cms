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
reference as the minimum a collection library needs.

The only project that references it is the test project, `tests/Struo.Tests`, and it does so
deeply — "Removing the sample completely" below lists every file that has to change.
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

With both edits done and the process restarted, CodeFirst creates `articles`,
`article_translations`, `tags`, `article_tags`, and `categories` — five tables, the same way it
creates a table for any other collection. `GET /api/schema` now lists eleven collections: the
sample's four (`article`, `articleTag`, `category`, `tag`) alongside the seven framework
collections; `ArticleTranslation` is a sidecar, not a collection of its own. `articleTag` is
`Hidden` in the admin sidebar, but `/api/schema` still lists it.

While the sample is opted in, one `dotnet test` test stays red:
`TemplateInvariantsTests.Host_project_has_no_project_reference_into_samples` reads
`Struo.Api.csproj` directly and asserts that it carries no `ProjectReference` into `samples` —
exactly the line the first step above just added. Getting back to green is step 1 of the removal
list below.

## File by file

Six `.cs` files; the lead collection comes first.

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

If the sample is enabled against an existing database whose `article_tags` table predates the
`note`/`sort` columns, they arrive only through Development's `Database:AutoSyncSchema=true` (see
[Chapter 21: Schema Management and Upgrades](21-schema-and-upgrades.md)) or a fork-authored
migration script — `db/migrations` holds only the fork's own scripts, never the sample collection's
schema.

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

The cleanup reaches wider than the `samples/Struo.Sample.Blog/` directory: the backend test suite
uses the sample's collections as a fixture. Work through the list below in order.

1. Revert both opt-in edits: remove the `ProjectReference` to `Struo.Sample.Blog.csproj` from
   `Struo.Api.csproj`, and re-comment (or delete) the `"ContentAssemblies"` line in your
   `appsettings.Development.json`. Then delete that line and the two comment lines above `"Struo"`
   from the tracked `appsettings.Development.json.example` too — otherwise the tracked example keeps
   pointing at a sample that no longer exists.

2. Delete the whole `samples/Struo.Sample.Blog/` directory; remove the `<Folder Name="/samples/">`
   block from `StruoCMS.slnx`; remove the `ProjectReference` to it from
   `tests/Struo.Tests/Struo.Tests.csproj`.

3. Delete `tests/Struo.Tests/Support/ContentAssemblyEnvBootstrap.cs` — the `[ModuleInitializer]`
   that sets `Struo__ContentAssemblies__0=Struo.Sample.Blog` into the process environment before
   any test runs; without it, the test host `ApiFactory` builds never see the sample's collections.

4. Find every test file that directly `using`s the sample's types, and delete them one by one:

   ```
   grep -rl "using Struo.Sample.Blog;" tests/Struo.Tests
   ```

   This lists 36 files; see [Chapter 22](22-testing.md) for how they're distributed.

5. Run `dotnet build` to confirm the build is clean — nothing under `src/Struo.*` references the
   sample. `dotnet test` is still red at this point; the next two steps handle it.

6. Fix `tests/Struo.Tests/Metadata/ConventionMetadataDiscoveryTests.cs`: two of its four tests set
   the string `"Struo.Sample.Blog"` as the assembly to scan — delete
   `Configured_content_assembly_is_scanned` and `Registers_entity_type_collector`, or point them at
   your own fork's assembly instead; the other two don't involve the sample and stay.

7. Delete the test files that carry no `using` for the sample's namespace but exercise
   `article`/`category`/`tag` over REST or GraphQL — they depend entirely on step 3's module
   initializer, so the compiler can't catch them. Run `dotnet test` once and look for
   `404`/`KeyNotFoundException` failures under `Api/`, `Query/`, `GraphQl/`, `Localization/`,
   and `Identity/`.

8. Clean up the four stale comments still mentioning the sample: `ApiFactory.cs` and
   `CorsAndCookieTests.cs` reference the now-deleted `ContentAssemblyEnvBootstrap.cs`;
   `FakeMetadataFixtures.cs` and `GraphQlExecutionTests.cs` have comments still pointing at the
   sample's paths and types.

9. In `frontend/`: delete `frontend/e2e/sample/` (8 spec files); remove the `"sample"`
   project and the now-unneeded `testIgnore: '**/e2e/sample/**'` for `core` from
   `frontend/playwright.config.ts`; remove `"e2e:sample"` and `"e2e:all"` from
   `frontend/package.json`, keeping only `"e2e"`; trim the sample's section out of
   `frontend/e2e/README.md`.

10. Clean up the five tables left behind in any database the sample has run against — CodeFirst
    creates them, but nothing drops them along with the code:

    ```
    DROP TABLE IF EXISTS article_tags, article_translations, articles, categories, tags CASCADE;
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
project, begin at [Chapter 3: Getting Started](03-getting-started.md).
