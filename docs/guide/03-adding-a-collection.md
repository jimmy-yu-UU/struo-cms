# Adding a Collection

A **Collection** is a content type StruoCMS exposes as a CRUD API. You define one by writing a plain
C# class decorated with `[CmsCollection]`. On startup StruoCMS scans for these classes, caches their
metadata, creates their tables (in Development), and serves them at `/api/schema` and
`/api/items/{collection}` — **no changes to `Program.cs` are needed.**

## Where to put the class

StruoCMS scans three places (see "How discovery works" below). The two you'll use:

- **Directly in the template (simplest):** put the class anywhere in the `Struo.Api` project. It is
  found automatically — no configuration.
- **In a separate content project:** put the class in its own project (e.g. the sample
  `Struo.Sample.Blog`) and list that assembly under `Struo:ContentAssemblies` in
  `appsettings.json`. The project must be referenced by `Struo.Api` so its DLL ships.

## A worked example

This defines a `Product` collection with a text field, a select field, a relation to `Category`, and
translated (per-locale) fields.

```csharp
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Api.Content; // any namespace in a scanned assembly

[SugarTable("products")]
[CmsCollection("Product", Icon = "package", Group = "Catalog", DefaultDisplayField = nameof(Sku))]
public sealed class Product : AuditableEntity
{
    // AuditableEntity declares Id as abstract — you MUST `override` it and mark the PK.
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "SKU", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Sku { get; set; } = string.Empty;

    [CmsField(Label = "Status", Interface = FieldInterface.Select, Sort = 2)]
    [CmsOptions("draft:Draft", "active:Active", "archived:Archived")]
    public string Status { get; set; } = "draft";

    [SugarColumn(IsNullable = true)]
    public Guid? CategoryId { get; set; }

    // Navigation properties are not columns: [SugarColumn(IsIgnore = true)] keeps them out of the table.
    [Navigate(NavigateType.OneToOne, nameof(CategoryId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public Category? Category { get; set; }

    [CmsTranslations(typeof(ProductTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<ProductTranslation> Translations { get; set; } = [];
}
```

`Category` above is any other `[CmsCollection]` you want to relate to (here, the sample blog's
`Category`). The mechanics of a relation are the `Guid? {Name}Id` foreign-key column plus the
`[Navigate]` + `[CmsRelation]` pair on the ignored navigation property.

The **translation sidecar** carries the per-locale fields. Its foreign key must follow the
convention `{Parent}Id`, and it must have `Id` and a string `Locale`:

```csharp
using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Seo; // SeoTranslation base gives per-locale SEO fields

namespace Struo.Api.Content;

[SugarTable("product_translations")]
public sealed class ProductTranslation : SeoTranslation
{
    // Sidecar PK is its own auto-increment long (distinct from the parent's Guid Id).
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid ProductId { get; set; }   // {Parent}Id convention (matches the parent class name + "Id")
    public string Locale { get; set; } = string.Empty;

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true)]
    public string Name { get; set; } = string.Empty;

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Description", Interface = FieldInterface.RichText)]
    public string? Description { get; set; }
}
```

## Attribute reference

| Attribute | Put it on | What it does |
|---|---|---|
| `[CmsCollection(label)]` | the class | Registers the collection. Optional `Icon`, `Group`, `DefaultDisplayField`. |
| `[CmsField(...)]` | a property | Makes the property an editable field. Set `Interface` (Text, RichText, Select, DateTime, Image, File, …), `Required`, `Searchable`, `Sortable`, `ReadOnly`, `Hidden`, `Sort`, `HelpText`, `Group`, `MaxLength` (see below). |
| `[CmsOptions("value:Label", …)]` | an option-type field | Supplies the choices for option-type interfaces (`Select`, `MultiSelect`, `Radio`, `CheckboxGroup`, `Tags`) — a Select without it just has no options. Placing it on a **non**-option field fails fast at startup. |
| `[CmsRelation(...)]` | a navigation property (also needs SqlSugar `[Navigate]`) | Exposes a relation (Dropdown / TreeSelect / RelatedList) with `DisplayTemplate`, `OnDelete`. |
| `[CmsTranslations(typeof(T))]` | a `List<T>` property | Declares the per-locale sidecar `T`; `T`'s `[CmsField]`s become translated fields. |

Properties with neither `[CmsField]` nor a relation attribute (e.g. `CategoryId`) are persisted but
not exposed as editable fields — they back relations.

## Field max length (`MaxLength`)

`[CmsField(MaxLength = 100)]` sets the CMS-layer input limit: the admin form caps typing at 100
characters and the API rejects longer values with 400. Undeclared short-string fields (Text, Slug,
Email, Url, Password, Color, Phone, and option-backed interfaces) default to **255** — matching the
database default; content-bearing fields (RichText, Textarea, Markdown, Code, Json) are unlimited.
Lengths count UTF-16 code units (what `string.Length` and JavaScript `.length` return). A negative
`MaxLength`, or one placed on a non-string property, fails fast at startup.

`MaxLength` is deliberately independent of the **database column width**, which SqlSugar controls
(`[SugarColumn(Length = n)]`, default `varchar(255)`; content-bearing interfaces map to `text`). If
you raise `MaxLength` above 255, also widen the column, or values in between will still fail at the
database:

```csharp
[CmsField(Label = "Summary", MaxLength = 500)]
[SugarColumn(Length = 500)]
public string Summary { get; set; } = string.Empty;
```

## How discovery works

At startup StruoCMS scans the union of:

1. The **framework assembly** (built-ins: users, roles, files, languages).
2. The **host assembly** — `Struo.Api` (passed by `Program.cs` as `typeof(Program).Assembly`). This
   is why a class dropped into `Struo.Api` needs no configuration.
3. Any assemblies named under **`Struo:ContentAssemblies`** in `appsettings.json`.

For each discovered `[CmsCollection]`, and in **Development only**, StruoCMS runs SqlSugar
`InitTables` to create/update the table (and the sidecar and any M2M junction tables). The entity
list is derived automatically from the scanned metadata — you never edit a table list.

## After you add a class

1. Restart the app (Development).
2. The table is created; the collection appears at `GET /api/schema` and is served at
   `GET/POST/PUT/DELETE /api/items/{collection}` (`{collection}` is the camelCase class name, e.g.
   `product`).

## Production note

`InitTables` runs in **Development only** — `DatabaseInitializer` refuses to run outside Development.
For production, apply the schema through a reviewed migration script (see
[Getting Started → InitTables](01-getting-started.md#development-only-inittables)).

## Querying relations (deep expansion)

A relation field can be expanded **multiple levels deep**, both over GraphQL and REST.

GraphQL — nest the selection and it resolves the whole chain:

```graphql
{ article(id: "...") { category { parent { name } } } }
```

REST — nest the `deep` JSON envelope the same way (`POST /api/items/article/query`):

```json
{ "deep": { "category": { "deep": { "parent": {} } } } }
```

Nesting depth is capped by `StruoQueryOptions.MaxRelationDepth` (default **5**); exceeding it, or naming an
unknown relation at any level, returns 400 (`BAD_USER_INPUT`). The flat query-string form
(`?deep=category,tags`) stays **single-level** — it has no syntax for nesting. Filtering, sorting, or
paginating a **nested relation list** (e.g. only the first 10 of a category's articles) is not yet
supported — a nested list currently returns all rows; that's planned for a future slice (8c.3b).
