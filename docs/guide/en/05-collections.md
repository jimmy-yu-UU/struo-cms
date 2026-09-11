# 5. Defining Collections

Adding a new content type to StruoCMS means writing a C# class in a particular shape — one that
becomes a database table, REST and GraphQL endpoints, and an admin form all at once. Adding a
collection touches no framework code; it only adds a class to your own content project, and its
table needs no migration script either — CodeFirst creates any missing table automatically, on any
backend, in any environment.

## A minimal collection

A collection is a class carrying `[CmsCollection]` (in `Struo.Domain.Metadata.Attributes`), which a
class can carry only once. The smallest class that compiles needs three things: a `[SugarTable]`, a
property carrying `[SugarColumn(IsPrimaryKey = true)]`, and at least one `[CmsField]`.

Inheriting `AuditableEntity` is the recommended shape, not a requirement — the framework's own
`Language` implements `IAuditable` directly, with a `long` auto-increment primary key. The sample's
`ArticleTag` has no base class at all.

The `Announcement` class below lives in a content project called `Acme.Content` — the name is yours
to choose (what the project needs to reference comes later in this chapter). The example adds a few
common settings beyond the bare minimum.

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

    [CmsField(Label = "Published At", Interface = FieldInterface.DateTime, Sort = 4)]
    public DateTime? PublishedAt { get; set; }
}
```

After restarting the API, this class becomes an `announcements` table, an Announcement entry under
the Content sidebar group, a set of REST and GraphQL endpoints, and an admin form with four inputs.
A freshly declared collection has no RBAC grants yet, so only a super-admin can see it at first.

`AuditableEntity` declares `Id` as `abstract`, so forgetting to override it is a compile error; the
override must carry `[SugarColumn(IsPrimaryKey = true)]`, because the inherited `Id` carries no
attribute and SqlSugar doesn't recognize it as the primary key. It also supplies
`CreatedAt`/`CreatedBy`/`UpdatedAt`/`UpdatedBy` — none of the four needs a `[CmsField]`; the scanner
turns them into system fields automatically.

`AuditableEntity` also has a `Version` for optimistic concurrency, which needs no
`[CmsField]` either. The `version` key in an item response appears only for a collection that
inherits `AuditableEntity` — a collection like `Language` or `ArticleTag` has none.

A property with no `[CmsField]` and a name other than the four audit names is skipped by the
scanner entirely, so you can keep ordinary properties of your own on the same entity. The framework
assigns a new item's primary key itself, as a version-7 GUID — you never generate one yourself.
Table names follow one convention: lower-case, plural, snake_case, written explicitly on
`[SugarTable]`. Column names follow SqlSugar's lower-casing of the CLR property name.

A nullable property (`DateTime?`, `Guid?`, `int?`, `string?`) maps to a nullable column
automatically — CodeFirst infers it, so `[SugarColumn(IsNullable = true)]` is redundant. Only the
reverse, forcing a column to be non-nullable, needs an explicit `[SugarColumn(IsNullable = false)]`.

## `[CmsCollection]` options

`[CmsCollection]` has these settable members:

| Member | Type | Default | Effect |
|---|---|---|---|
| `Label` | `string` | constructor argument | sidebar and page title |
| `Icon` | `string?` | `null` | sidebar icon |
| `Group` | `string?` | `null` | sidebar navigation group |
| `DefaultDisplayField` | `string?` | `null` | item display title |
| `AdminOnly` | `bool` | `false` | generic CRUD writes always require super-admin |
| `Hidden` | `bool` | `false` | hide from sidebar |
| `Revisions` | `bool` | `false` | enable revision history |

`DefaultDisplayField` must name a real field — the match is against the camelCased field name — and
a name that doesn't resolve throws `MetadataException` at startup.

`AdminOnly = true` governs only generic CRUD writes: it requires a super-admin regardless of
whatever RBAC grants the collection has; reads still follow ordinary RBAC.

Soft delete and revisions are two independent opt-ins: soft delete comes from the collection
implementing `ISoftDeletable`, and revisions come from setting `Revisions = true` on
`[CmsCollection]`.

## `[CmsField]` options

Every `[CmsField]` setting is written as a named argument:

| Member | Type | Default | Effect |
|---|---|---|---|
| `Label` | `string?` | `null` | field label; falls back to PascalCase property name |
| `Interface` | `FieldInterface` | `Text` | decides field type, GraphQL type, admin editor |
| `Display` | `string?` | `null` | no effect |
| `Required` | `bool` | `false` | required check (see below) |
| `Searchable` | `bool` | `false` | added to full-text search whitelist |
| `Sortable` | `bool` | `false` | admin list header becomes clickable to sort |
| `Sort` | `int` | `0` | field order in the form and list columns |
| `ReadOnly` | `bool` | `false` | read-only protection; disables admin input |
| `Hidden` | `bool` | `false` | hidden from schema/API; write unaffected |
| `HelpText` | `string?` | `null` | help text under the admin form input |
| `Translatable` | `bool` | `false` | merged into the translation sidecar |
| `Group` | `string?` | `null` | the `[CmsFieldGroup]` it belongs to |
| `MaxLength` | `int` | `0` (unset) | input length cap, in UTF-16 code units |

- `Interface` has no CLR-type inference at all — a `[CmsField]` on an `int` with no `Interface` set
  is a `Text` field, not `Number`.
- `Sort` decides its order in the admin form and list columns — ascending by `Sort`, with
  declaration order as the tiebreaker; the four audit fields are fixed at `Sort = 1000`, so only a
  field declaring a higher `Sort` sorts after them.
- `MaxLength` is a CMS-layer input-length cap, independent of the database column width.

`Required` validates the merged entity on update, not the request body: omitting a field keeps the
stored value and passes, while an explicit `null` or a blank string (including whitespace-only)
fails. On create, the field must always be supplied. For a non-nullable `Guid` field, `Guid.Empty`
also counts as missing, and such a field can never be sent as `null` at all.

A field with `Hidden = true` never joins the search whitelist, even with `Searchable` set.
`Sortable` decides only whether the admin list header can be clicked to sort; the query DSL's
`sort` ignores the flag — any known field can be sorted on.

A field's external key is the camelCase form of the CLR property name. A label that falls back
to the PascalCase property name gets no inserted spaces and no case changes.

### Name restrictions

A field or relation name can't be any of these five words, with or without a leading underscore
(`and` and `_and` both count), compared case-insensitively — the query DSL needs them for itself:

- `and`
- `or`
- `some`
- `none`
- `junction`

Using a reserved word is caught at startup with `MetadataException`.

## Field groups and option lists

`[CmsFieldGroup]` sits on the class, and the same class can carry it more than once, one group per
declaration. Its members are `Name` (constructor argument), `Label`, and `Sort`. A field joins a
group with `[CmsField(Group = "SameName")]`. The sample's `Article` declares two groups, `Content`
and `SEO`: its own fields use `Group = "Content"`, and the three SEO fields inherited from its
translation sidecar use `Group = "SEO"`.

Groups are returned to REST and GraphQL callers, but the default admin form doesn't section by
group — it only buckets non-system fields into shared and translatable, each sorted by `Sort`, with
the translatable bucket placed into per-language tabs.

`CmsOptionsAttribute` takes a `params string[]`; each entry is either `"value:label"` or a bare
`"value"` (whose label falls back to the value itself) — the split looks only at the **first**
colon. A blank option value fails the scan. `[CmsOptions]` is legal on exactly five interfaces, and
anything else is also a startup failure:

- `Select`
- `MultiSelect`
- `Radio`
- `CheckboxGroup`
- `Tags`

In practice the first four almost always declare an option list — without one there's nothing to
pick from — while `Tags` is usually left free-form. The sample's `Article.Keywords` is a `Tags`
field with no options declared; `Article.Regions` and `Article.Audiences` both have one.

## Where content projects live and how they are found

Your content assembly must be listed in `Struo:ContentAssemblies`, and the API host project must
carry a `ProjectReference` to it — both are needed, and a DLL merely sitting alongside the host
doesn't get loaded. An entry that fails to resolve fails startup with a message naming that entry
(see [Chapter 4: Configuration Reference](04-configuration.md)). Content libraries live outside
`src/Struo.*`.

A content project is an ordinary class library. At minimum it needs a reference to `Struo.Domain`
(for the attributes and enums) and the `SqlSugarCore` package (so `[SugarTable]`/`[SugarColumn]`
work). The scan runs only once, at startup, so adding a new content project needs a restart to take
effect; and even when a type fails to load from some assembly, the scan just keeps the types that
did load rather than aborting entirely.

## Creating the tables

CodeFirst creates a collection's own table, its translation sidecar's table, and any many-to-many
junction table it declares — the same on all five backends, in Development and Production alike.
The framework's own tables are created alongside your collections, whichever ones are missing.
Table creation only touches tables that don't exist yet — an existing table is never touched at
all.

Changing a table that already holds data goes through migration instead: `Database:MigrationsPath`
points to your own reviewed SQL scripts, and `Database:AutoSyncSchema` — Development-only — lets
CodeFirst alter an existing table directly; setting it in any other environment has no effect. The
full semantics and defaults of both keys are in
[Chapter 4: Configuration Reference](04-configuration.md).

## Checklist

Adding a collection, in order:

1. Put the content library outside `src/Struo.*`.
2. Put `[SugarTable]` and `[CmsCollection]` on the entity, with a primary key carrying
   `[SugarColumn(IsPrimaryKey = true)]`.
3. Add `[CmsField]` to every property you want to expose, and `[CmsOptions]` on any interface that
   needs an option list.
4. Implement `ISoftDeletable`, or set `Revisions = true`, if you need them.
5. Add `[CmsRelation]` and `[Navigate]` if you have relations (see
   [Chapter 8: Relations](08-relations.md)).
6. Add the project reference, and add the assembly to `Struo:ContentAssemblies`.
7. Restart the process so the scan picks up the new collection.
8. Grant RBAC permissions, or only a super-admin can use it.

## What's next

With a collection declared, the next step is choosing the right interface for each field — the
subject of [Chapter 6: Field Types and Editors](06-field-types.md).
