# 5. Defining Collections

Adding a new content type to StruoCMS means writing a C# class in a particular shape — one that
becomes a database table, REST and GraphQL endpoints, and an admin form all at once. Adding a
collection touches no framework code; it adds a class to your own content project, plus the
migrations that create its tables. The API never builds a table from the class: `make:migration`
generates each migration from the class, and `migrate` applies them.

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

Once its migration is generated and applied (see "Creating the tables" below) and the API
restarted, this class becomes an `announcements` table, an Announcement entry under the Content
sidebar group, a set of REST and GraphQL endpoints, and an admin form with four inputs. A freshly
declared collection has no RBAC grants yet, so only a super-admin can see it at first.

In Development, a collection whose table does not exist yet fails the startup schema check, which
reports the missing table and refuses to start.

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
Base table names follow one convention: lower-case, plural, snake_case, written explicitly on
`[SugarTable]`; the framework's own tables additionally carry `Database:TablePrefix` (see
[Chapter 4: Configuration Reference](04-configuration.md)), your collections do not. Column names
follow SqlSugar's lower-casing of the CLR property name.

A nullable property (`DateTime?`, `Guid?`, `int?`, `string?`) maps to a nullable column
automatically: the entity metadata records it, `make:migration` writes `.Nullable()` from it, and
`migrate:check` compares against it, so `[SugarColumn(IsNullable = true)]` is redundant. Only the
reverse, forcing a column to be non-nullable, needs an explicit `[SugarColumn(IsNullable = false)]`.

## `[CmsCollection]` options

`[CmsCollection]` has these settable members:

| Member | Type | Default | Effect |
|---|---|---|---|
| `Label` | `string` | constructor argument | sidebar and page title |
| `Icon` | `string?` | `null` | sidebar icon, names below |
| `Group` | `string?` | `null` | sidebar navigation group |
| `Name` | `string?` | camelCased class name | collection name, see below |
| `DefaultDisplayField` | `string?` | `null` | item display title |
| `AdminOnly` | `bool` | `false` | generic CRUD writes always require super-admin |
| `Hidden` | `bool` | `false` | sidebar only; REST, GraphQL, RBAC and schema unaffected |
| `Revisions` | `bool` | `false` | enable revision history |

`Name` is the identity of the collection: the REST route segment, the GraphQL names, and the value
stored in permission grants, revisions and other rows. Left unset, it is the camelCased class name,
so renaming the class silently orphans that data. A fork should set `Name` on each of its collections
before they hold data. It must match `^[a-z][A-Za-z0-9]*$` and be unique across all collections,
framework ones included; anything else throws `MetadataException` at startup. The names the GraphQL
schema derives from it must not collide with another collection's or with a built-in name either
(see [Chapter 14: GraphQL](14-graphql.md)); that also stops startup. `Name` does not
change the translation sidecar convention: a sidecar's foreign key property is still `{ClassName}Id`,
so it must be renamed together with the class.

`DefaultDisplayField` must name a real field — the match is against the camelCased field name — and
a name that doesn't resolve throws `MetadataException` at startup.

`AdminOnly = true` governs only generic CRUD writes: it requires a super-admin regardless of
whatever RBAC grants the collection has; reads still follow ordinary RBAC.

Soft delete and revisions are two independent opt-ins: soft delete comes from the collection
implementing `ISoftDeletable`, and revisions come from setting `Revisions = true` on
`[CmsCollection]`.

### Choosing a sidebar icon

The value is looked up in `ICON_MAP`, the fixed table in `frontend/src/lib/icons.ts`; the names
available today include `article`, `tag`, `folder`, `megaphone`, `image`, `file`, `user`, and
`table`.

A name the table doesn't have, or no name at all, falls back to a generic file icon — the sidebar
never breaks over a typo. Adding an icon the table doesn't have means adding a key to `ICON_MAP` —
a change in the admin SPA, covered by admin SPA customization.

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

The scan always covers the API host project itself and the framework's own assembly — content
classes can live directly in the host project. A separate class library instead needs a
`ProjectReference` from the host, plus its assembly name in `Struo:ContentAssemblies`; a DLL merely
sitting alongside the host doesn't get loaded.

Either way works; where the classes live is your choice. An entry that fails to resolve fails
startup with a message naming it (see [Chapter 4: Configuration Reference](04-configuration.md)).

A content project is an ordinary class library. If it only declares entities, the minimum is a
reference to `Struo.Domain` (for the attributes and enums) and the `SqlSugarCore` package (so
`[SugarTable]`/`[SugarColumn]` work). If it also holds its own migrations, it needs
`Struo.Infrastructure` too, because a migration derives from its `StruoMigration`; the sample
project is set up that way.

The scan runs only once, at startup, so adding a new content project needs a restart to take
effect; and even when a type fails to load from some assembly, the scan just keeps the types that
did load rather than aborting entirely.

## Creating the tables

Every table comes from a migration. A collection can need several tables: its own, its translation
sidecar's, and one for each many-to-many junction it declares. Run `make:migration` once per table,
passing that table's entity type name to `--entity`:

```bash
dotnet run --project src/Struo.Api -- make:migration CreateAnnouncements --entity Announcement --output <dir>
```

The `--output` directory must already exist, and a relative path starts from `src/Struo.Api`. The
migration must live in a scanned assembly: the API host project itself, or a class library listed
in `Struo:ContentAssemblies`.

Review the generated file, read the SQL it will run with `migrate:preview`, then run `migrate`:

```bash
dotnet run --project src/Struo.Api -- migrate
```

To change a table that already holds data, write a new migration; never edit one that has already
been applied. The rules, and how to split a change into steps, are in
[Chapter 21, "Writing a migration"](21-schema-and-upgrades.md#writing-a-migration).

## Checklist

Adding a collection, in order:

1. Decide where the content classes live: in the API host project itself, or in a class library the
   host references.
2. Put `[SugarTable]` and `[CmsCollection]` on the entity, with a primary key carrying
   `[SugarColumn(IsPrimaryKey = true)]`.
3. Add `[CmsField]` to every property you want to expose, and `[CmsOptions]` on any interface that
   needs an option list.
4. Implement `ISoftDeletable`, or set `Revisions = true`, if you need them.
5. Add `[CmsRelation]` and `[Navigate]` if you have relations (see
   [Chapter 8: Relations](08-relations.md)).
6. Add the project reference, and add the assembly to `Struo:ContentAssemblies` (only for a
   separate class library).
7. Run `make:migration --entity` once per new table, review the generated files, then run
   `migrate`.
8. Restart the process so the scan picks up the new collection.
9. Grant RBAC permissions, or only a super-admin can use it.

## What's next

With a collection declared, the next step is choosing the right interface for each field — the
subject of [Chapter 6: Field Types and Editors](06-field-types.md).
