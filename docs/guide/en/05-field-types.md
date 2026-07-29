# 5. Field Types & Interfaces

Every `[CmsField]` names a `FieldInterface` (`src/Struo.Domain/Metadata/Enums/FieldInterface.cs`). That
one enum value drives three independent, separately-implemented things at once, and this chapter is
about all three:

1. **The database column** — decided at CodeFirst table-creation time by
   `SqlSugarClientFactory`'s `EntityService` hook (`src/Struo.Infrastructure/Persistence/SqlSugarClientFactory.cs`),
   which inspects each property's `[CmsField]` interface to widen or JSON-flag the column.
2. **The CMS-layer metadata** exposed over REST/GraphQL — built by
   `MetadataScanner.BuildField` (`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`), which resolves
   the effective `MaxLength` and validates interface-specific constraints (options, Repeater sub-fields).
3. **The admin-SPA editor** — chosen by `frontend/src/lib/fieldTypes/registry.ts`, a single map from
   each camelCase interface name to one Vue field component. Its companion `types.ts` states explicitly
   that this union "mirrors backend `Struo.Domain.Metadata.Enums.FieldInterface`... MUST be kept in sync
   with that enum."

One asymmetry worth flagging before the table: `Json` and the six other structured interfaces
(`MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/`Files`/`Repeater`) both end up in a `text` column, but
for different reasons. The latter six are `JsonColumnInterfaces` — SqlSugar is told `IsJson = true` and
(de)serializes the CLR `List<>`/`Dictionary<>` automatically. `Json` is deliberately **not** one of
them: it's a plain `string` property holding raw JSON text, widened to `text` only because it's
content-bearing; the API layer manually re-parses that raw text into a `JsonElement` on read
(`ItemProjector.Project`) and manually re-serializes it on write (`ItemDeserializer.Deserialize`) — no
SqlSugar-level JSON magic is involved for this one interface.

## Full table of all 33 values

"(a)" = SqlSugar's default CodeFirst mapping for the CLR type (`string` → `varchar(255)` unless
widened or explicitly overridden). "(b)" = widened to `text` automatically because the interface is
content-bearing. "(c)" = stored as JSON text (`IsJson = true`, `text` column); SqlSugar (de)serializes
the CLR collection automatically.

| Interface | Purpose | CLR type expected | Resulting column | Admin editor |
|---|---|---|---|---|
| `Text` | Short plain string | `string` | (a) `varchar(255)` | `TextField` (PrimeVue `InputText`) |
| `Textarea` | Multi-line plain string | `string` | (b) `text` | `TextareaField` |
| `RichText` | Sanitized HTML | `string` | (b) `text` | `RichTextField` (TipTap) |
| `Markdown` | Markdown source text | `string` | (b) `text` | `TextareaField` — plain textarea, no live preview or toolbar |
| `Code` | Source-code text | `string` | (b) `text` | `TextareaField` — plain textarea, no syntax highlighting |
| `Slug` | URL-safe short string | `string` | (a) `varchar(255)` | `TextField` |
| `Email` | Email address | `string` | (a) `varchar(255)` | `TextField` — no email-format check beyond `Required` |
| `Url` | URL | `string` | (a) `varchar(255)` | `TextField` |
| `Password` | Secret value | `string` | (a) `varchar(255)` | `TextField` — plain (unmasked) input; excluded from the GraphQL schema entirely |
| `Color` | Color value | `string` | (a) `varchar(255)` | `TextField` — plain text input, no swatch picker |
| `Phone` | Phone number | `string` | (a) `varchar(255)` | `TextField` |
| `Number` | Numeric value | `int`/`long`/`decimal`/`double`/etc. | (a) whatever SqlSugar maps that numeric CLR type to | `NumberField` (PrimeVue `InputNumber`) |
| `Slider` | Numeric value | numeric | (a) as above | `NumberField` — same component as `Number`, no slider widget |
| `Rating` | Numeric value | numeric | (a) as above | `NumberField` — same component as `Number`, no star widget |
| `Boolean` | True/false | `bool` | (a) boolean | `BooleanField` |
| `Checkbox` | True/false | `bool` | (a) boolean | `BooleanField` — same component as `Boolean` |
| `Date` | Calendar date | `DateTime`/`DateTime?` | (a) SqlSugar's default `DateTime` mapping | `DateField`, date-only picker |
| `Time` | Time of day | `DateTime`/`DateTime?` | (a) as above | `DateField`, time-only picker |
| `DateTime` | Date and time | `DateTime`/`DateTime?` | (a) as above | `DateField`, date + time picker |
| `Select` | Single choice from a fixed list | `string` | (a) `varchar(255)` | `SelectField` — needs `[CmsOptions]` |
| `MultiSelect` | Multiple choices from a fixed list | `List<string>` | (c) JSON in `text` | `MultiSelectField` — needs `[CmsOptions]` |
| `Radio` | Single choice from a fixed list | `string` | (a) `varchar(255)` | `RadioField` — needs `[CmsOptions]` |
| `CheckboxGroup` | Multiple choices from a fixed list | `List<string>` | (c) JSON in `text` | `CheckboxGroupField` — needs `[CmsOptions]` |
| `Tags` | Free-form multi-value tags | `List<TagItem>` | (c) JSON in `text` | `TagsField` — `[CmsOptions]` allowed but not required |
| `Json` | Arbitrary JSON value | `string` (raw JSON text) | (b) `text` — see the note above, not `IsJson` | `JsonField` |
| `KeyValue` | Free-form string map | `Dictionary<string, string>` | (c) JSON in `text` | `KeyValueField` |
| `Repeater` | Repeatable child object | `List<TChild>`, `TChild` a plain class with its own `[CmsField]`s | (c) JSON in `text` | `RepeaterField` |
| `File` | Single file reference | `Guid`/`Guid?` | (a) `uuid` | `FileField` (generic picker) |
| `Image` | Single image reference | `Guid`/`Guid?` | (a) `uuid` | `FileField` (picker with image preview) |
| `Files` | Multiple file references | `List<Guid>` | (c) JSON in `text` | `FilesField` |
| `Hidden` | Own-field never meant to be admin-editable or GraphQL-exposed | not enforced by the scanner | (a) whatever the CLR type maps to | `ReadonlyField` — display-only; excluded from the GraphQL schema |
| `Divider` | Visual separator between fields | not enforced by the scanner; the component ignores the field's value | (a) whatever the CLR type maps to (or none, with `[SugarColumn(IsIgnore = true)]`) | `DividerField` — renders a static `<hr>` |
| `Uuid` | Read-only identifier display | `Guid`/`Guid?` | (a) `uuid` | `ReadonlyField` |

This table's 33 rows are exactly the 33 `FieldInterface` enum members, each appearing once, in
declaration order, verified directly against `src/Struo.Domain/Metadata/Enums/FieldInterface.cs`.

Repeater sub-fields are restricted to a smaller, scalar-only allowlist —
`Text`/`Textarea`/`Markdown`/`Code`/`Slug`/`Email`/`Url`/`Color`/`Phone`/`Number`/`Slider`/`Rating`/
`Boolean`/`Checkbox`/`Date`/`Time`/`DateTime`/`Select`/`Radio` — and cannot be `Translatable`;
`MetadataScanner.BuildRepeaterChildFields` fails the scan otherwise. Nesting a `Repeater` inside another
`Repeater`, and `RichText`/`File`/`Image`/`Files`/`Password`/`Hidden`/`Uuid`/`Divider`/`Json`/`KeyValue`
sub-fields, are all rejected the same way.

## `MaxLength` behavior

`[CmsField(MaxLength = n)]` is a **CMS-layer** input-length limit (UTF-16 code units) — independent of
the actual database column width (`[SugarColumn(Length = n)]`) or an explicit
`[SugarColumn(ColumnDataType = ...)]`. `MetadataScanner.BuildField` throws `MetadataException` at
startup if `MaxLength` is negative, or if it's set (`> 0`) on a non-`string` property.

The effective value exposed as `FieldMetadata.MaxLength` (and, from there, to the admin SPA and any API
client) resolves as:

1. The explicit `MaxLength`, if set (`> 0`).
2. Otherwise, `255`, if the property is a `string` **and** the interface is one of the twelve
   "short-string" interfaces: `Text`, `Slug`, `Email`, `Url`, `Password`, `Color`, `Phone`, `Select`,
   `MultiSelect`, `Radio`, `CheckboxGroup`, `Tags`. (`MultiSelect`, `CheckboxGroup` and `Tags` only
   matter here if you unusually give them a plain `string` property instead of the
   `List<string>`/`List<TagItem>` they normally use; `Radio`, like `Select`, normally *is* a plain
   `string` already — see the table above.)
3. Otherwise, `null` (unlimited) — every content-bearing interface (`Textarea`, `RichText`, `Markdown`,
   `Code`, `Json`) and every non-`string` field.

The admin SPA's `TextField.vue` passes `field.maxLength` straight through to PrimeVue `InputText`'s
native `maxlength` attribute, so this default of `255` is enforced client-side as a hard input cap even
when no `[CmsField]` ever set it explicitly. It has no bearing on the database column's actual width —
the two mechanisms only happen to agree on the number `255` because SqlSugar's own CodeFirst default for
an unwidened `string` column is also `varchar(255)`.

## Pitfalls

**An undeclared `string` becomes `varchar(255)`, so long-text fields need an explicit column type.**
Cause: a `string` property with no `[SugarColumn(ColumnDataType = ...)]` and an interface outside the
five content-bearing ones (`RichText`/`Textarea`/`Markdown`/`Code`/`Json`) keeps SqlSugar's CodeFirst
default of `varchar(255)`. Symptom: inserting a value longer than 255 characters fails on Postgres with
`22001 value too long for type character varying(255)`. Fix: use one of the five content-bearing
interfaces (auto-widened to `text`), or add an explicit `[SugarColumn(ColumnDataType = "text")]` — as
`SiteSettings.BrandName` and `Revision.Snapshot` do (`src/Struo.Infrastructure/Settings/SiteSettings.cs`,
`src/Struo.Infrastructure/Revisions/Revision.cs`).

**`IsJson` without a `text` column truncates.** Cause: `[SugarColumn(IsJson = true)]` alone leaves the
CodeFirst column length unset, which Postgres defaults to `varchar(1)`. Symptom: serializing any JSON
value longer than one character fails to insert (`22001`), even though the field "worked" against
SQLite in tests (SQLite ignores declared length; the bug only surfaces against Postgres). Fix: always
pair `IsJson = true` with an explicit `text` `DataType` — exactly what the framework's own
`JsonColumnInterfaces` hook does automatically for `MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/
`Files`/`Repeater` fields; if you ever set `[SugarColumn(IsJson = true)]` yourself on a property outside
that convention, set `ColumnDataType = "text"` alongside it.

**A `JsonElement` read back after its `JsonDocument` is disposed throws.** Cause: `System.Text.Json`'s
`JsonElement` is only valid as long as the `JsonDocument` that produced it is alive; parsing a `Json`
field's raw stored text with `using var doc = JsonDocument.Parse(raw)` and then returning or storing
`doc.RootElement` beyond that `using` block's scope throws `ObjectDisposedException` the next time it's
read. Fix: use `JsonSerializer.Deserialize<JsonElement>(raw)` instead — no `using`, no disposal, a
self-contained `JsonElement` safe to hold — exactly what `ItemProjector.Project` does when re-hydrating a
`Json` field for the API response.

**Multi-value selects need `IsJson` *and* a `text` column — one alone isn't enough.** Cause:
`MultiSelect`/`CheckboxGroup` (`List<string>`) rely on both settings together: `IsJson = true` tells
SqlSugar to (de)serialize the list at all, and `ColumnDataType = "text"` gives it room (see the previous
two pitfalls — either one alone reproduces the corresponding failure). The framework's `[CmsField]`-aware
CodeFirst hook applies both automatically for these interfaces; if you ever hand-declare the SqlSugar
attributes yourself instead of relying on that convention (for example, on a property the hook doesn't
reach), you must set both.

## Read-only, hidden and system fields

**`ReadOnly` (`[CmsField(ReadOnly = true)]`)** — the value is returned normally on read. On update it is
fully protected: `ItemService.UpdateCoreAsync`'s field overlay skips every `ReadOnly`/`IsSystem` field
outright, so an update body can never move a `ReadOnly` field's value onto the existing entity, whatever
its CLR type. On create, `ItemDeserializer.Deserialize` nulls a bound `ReadOnly`/`IsSystem` property
right after deserializing the request body — but, per its own comment, "only nullable props can be
nulled" (`canBeNull = !pi.PropertyType.IsValueType || Nullable.GetUnderlyingType(...) is not null`): a
`ReadOnly` field backed by a non-nullable value type (e.g. `[CmsField(ReadOnly = true)] public int
Views`) is left holding whatever the client supplied on create, since there is nothing to null it back
to. The admin SPA's `FieldInput.vue` also disables the rendered input
(`props.disabled === true || props.field.readOnly`) independently of both server-side mechanisms.

**`Hidden` (`[CmsField(Hidden = true)]`)** — independent of which `FieldInterface` the field uses (the
sample's `Article.InternalNote` is a `Text` field with `Hidden = true`). Effects, all in
`src/Struo.Application`:

- Stripped from `GET /api/schema` / `GET /api/schema/{collection}` entirely
  (`SchemaService.WithoutHiddenFields`) — invisible to any API/GraphQL client doing schema discovery,
  including the admin SPA itself.
- Excluded from the GraphQL schema (`CollectionSchemaBuilder` skips every `f.Hidden` field).
- Excluded from the projected item response (`ItemProjector` skips `field.Hidden`) —
  `GET /api/items/{collection}/{id}` never returns its value.
- Excluded from the query DSL's known-field, searchable-field and sortable-field whitelists
  (`QueryValidator`) — it cannot be filtered, searched or sorted on.
- Redacted from any revision snapshot returned externally (`RevisionSnapshotRedactor`), even though the
  raw revision row still captures the full value so a revert can restore it.
- **Not** excluded from the write overlay in `ItemService.UpdateCoreAsync` — a client that already knows
  the field's name can still set it via a normal `PUT`/`POST`. It simply never sees the value come back
  and never discovers the field's name through schema introspection or a normal item response.

Because the admin SPA only ever learns about fields through the schema/item responses above, a `Hidden`
field's metadata and value never reach it at all — there's nothing for
`frontend/src/lib/fieldTypes/registry.ts` to render, independent of whatever interface the field
declares.

**System fields** — `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` from `IAuditable`/
`AuditableEntity` need no `[CmsField]` at all: `MetadataScanner.BuildSystemField` adds them
automatically, with `IsSystem = true`, `ReadOnly = true`, `Sort = 1000` (after every declared field), and
an `Interface` picked from the property's CLR type — `DateTime` for `CreatedAt`/`UpdatedAt` (both
`DateTime`), `Text` for `CreatedBy`/`UpdatedBy` (both `Guid?`, which falls through
`BuildSystemField`'s `DateTime`/`DateTime?` check to the `Text` default). Unlike a plain `ReadOnly`
field, these four are fully protected on create too, regardless of `CreatedAt`/`UpdatedAt` being a
non-nullable `DateTime` — but by two different mechanisms depending on the operation:
`AuditAop.Register`'s `DataExecuting` hook unconditionally overwrites all four on insert, independent of
`ItemDeserializer`'s nullability-gated strip; on update, that same hook only re-stamps `UpdatedAt`/
`UpdatedBy` (its `UpdateByObject` branch has no case for `CreatedAt`/`CreatedBy`), so those two are
instead protected the same way every other `ReadOnly`/`IsSystem` field is on update — by
`ItemService.UpdateCoreAsync`'s field-overlay skip, which never copies them from the incoming body at
all. They're returned on read like any other field (`ItemProjector` does not skip `IsSystem`), and excluded
from both the admin item form and the collection-list columns (`frontend/src/lib/splitFields.ts` and
`frontend/src/lib/selectListColumns.ts` both filter `isSystem` out) — so, unlike `Hidden` fields, they
remain fully visible over the API; the shipped admin SPA simply never renders them.

## Adding a custom field editor

The interface-to-component mapping lives entirely in `frontend/src/lib/fieldTypes/registry.ts`, keyed by
the `FieldInterface` union declared in `frontend/src/lib/fieldTypes/types.ts` — a union its own comment
says must be kept in sync with the backend enum above. Building a genuinely new field editor, or
replacing one of the shipped components, is chapter 14's subject.

## Next steps

- Chapter 4, [Defining a Collection](04-defining-a-collection.md), for `[CmsField]`'s other options and
  how a collection ties its fields together.
- Chapter 6, [Internationalization](06-internationalization.md), for `Translatable` fields.
- Chapter 8, [Query DSL](08-query-dsl.md), for how `Searchable`/`Sortable`/`Hidden` shape the filter and
  sort whitelist.
- Chapter 10, [GraphQL API](10-graphql-api.md), for the full interface-to-SDL-type mapping.
- Chapter 14, [Admin SPA Customization](14-admin-spa-customization.md), to add a new field editor.
