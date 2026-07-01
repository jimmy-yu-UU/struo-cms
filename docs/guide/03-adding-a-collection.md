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

[CmsCollection("Product", Icon = "package", Group = "Catalog", DefaultDisplayField = "Sku")]
public sealed class Product : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)]
    public Guid Id { get; set; }

    [CmsField(Label = "SKU", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Sku { get; set; } = string.Empty;

    [CmsField(Label = "Status", Interface = FieldInterface.Select, Sort = 2)]
    [CmsOptions("draft:Draft", "active:Active", "archived:Archived")]
    public string Status { get; set; } = "draft";

    public Guid? CategoryId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(CategoryId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}", OnDelete = OnDelete.SetNull)]
    public Category? Category { get; set; }

    [CmsTranslations(typeof(ProductTranslation))]
    public List<ProductTranslation> Translations { get; set; } = [];
}
```

The **translation sidecar** carries the per-locale fields. Its foreign key must follow the
convention `{Parent}Id`, and it must have `Id` and a string `Locale`:

```csharp
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Seo; // SeoTranslation base gives per-locale SEO fields

namespace Struo.Api.Content;

public sealed class ProductTranslation : SeoTranslation
{
    public long Id { get; set; }
    public Guid ProductId { get; set; }   // {Parent}Id convention
    public string Locale { get; set; } = string.Empty;

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true)]
    public string Name { get; set; } = string.Empty;

    [CmsField(Label = "Description", Interface = FieldInterface.RichText)]
    public string? Description { get; set; }
}
```

## Attribute reference

| Attribute | Put it on | What it does |
|---|---|---|
| `[CmsCollection(label)]` | the class | Registers the collection. Optional `Icon`, `Group`, `DefaultDisplayField`. |
| `[CmsField(...)]` | a property | Makes the property an editable field. Set `Interface` (Text, RichText, Select, DateTime, Image, File, …), `Required`, `Searchable`, `Sortable`, `ReadOnly`, `Hidden`, `Sort`, `HelpText`, `Group`. |
| `[CmsOptions("value:Label", …)]` | a Select-type property | Defines the dropdown options. |
| `[CmsRelation(...)]` | a navigation property (also needs SqlSugar `[Navigate]`) | Exposes a relation (Dropdown / TreeSelect / RelatedList) with `DisplayTemplate`, `OnDelete`. |
| `[CmsTranslations(typeof(T))]` | a `List<T>` property | Declares the per-locale sidecar `T`; `T`'s `[CmsField]`s become translated fields. |

Properties with neither `[CmsField]` nor a relation attribute (e.g. `CategoryId`) are persisted but
not exposed as editable fields — they back relations.

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
