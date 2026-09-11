# 6. Field Types and Editors

When you're choosing an interface for a field and don't know what column it becomes or what the
admin form will look like, this chapter has the answer. A `[CmsField]`'s `Interface` value decides
three things at once: the database column, the field information REST and GraphQL expose, and
which admin editor renders it.

## Interface overview

Interfaces fall into seven groups by purpose, one table each. A relation is not a field
interface — it's declared on `[CmsRelation]`; see [Chapter 8: Relations](08-relations.md). An
interface must be written explicitly: leaving it unset falls back to `Text` (see
[Chapter 5: Defining Collections](05-collections.md)).

The translatable restriction: `MultiSelect`, `CheckboxGroup`, `Tags`, and `Repeater` child fields
can't be set translatable; everything else is in
[Chapter 7: Multilingual Content](07-i18n.md).

The tables below use a few shorthand terms under Database column:

- `varchar(255)`: SqlSugar CodeFirst's default width for `string`.
- Long text: the result of applying `ColumnShape.LongText`; each backend maps it to its own type.
- JSON: long text plus `IsJson = true` — SqlSugar serializes and restores the whole CLR collection
  directly.
- CLR default: numbers, `bool`, `DateTime`, `Guid` keep SqlSugar's ordinary mapping; nothing widens
  the length.
- `ColumnShape` has only two members, `LongText` and `TimestampWithTimeZone` — there's no numeric
  shape.

### Text

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Text` | `string` | `varchar(255)` | `TextField` |
| `Textarea` | `string` | Long text | `TextareaField` |
| `RichText` | `string` | Long text | `RichTextField` |
| `Markdown` | `string` | Long text | `TextareaField` |
| `Code` | `string` | Long text | `TextareaField` |
| `Slug` | `string` | `varchar(255)` | `TextField` |
| `Email` | `string` | `varchar(255)` | `TextField` |
| `Url` | `string` | `varchar(255)` | `TextField` |
| `Password` | `string` | `varchar(255)` | `TextField` |
| `Color` | `string` | `varchar(255)` | `TextField` |
| `Phone` | `string` | `varchar(255)` | `TextField` |

Five interfaces get their `string` property widened to long text automatically:
`Textarea`, `RichText`, `Markdown`, `Code`, and `Json` (covered later); every other `string`
interface stops at `varchar(255)`. Nothing checks format beyond `Required` and `MaxLength` —
`Email`, `Url`, and `Phone` are no exception.

`Markdown` and `Code` are both plain text boxes, with no preview or syntax highlighting.
`Password`'s input isn't masked, and it never appears in the GraphQL schema. `Color` is likewise a
plain text input, with no color-swatch picker.

### Numbers and booleans

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Number` | `int`/`long`/`decimal`/`double` | CLR default | `NumberField` |
| `Slider` | Numeric | CLR default | `NumberField` |
| `Rating` | Numeric | CLR default | `NumberField` |
| `Boolean` | `bool` | CLR default | `BooleanField` |
| `Checkbox` | `bool` | CLR default | `BooleanField` |

`Slider` and `Rating` reuse `Number`'s editor — there's no slider or star-rating component in the
admin. `Checkbox` reuses `Boolean`'s editor.

### Date and time

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Date` | `DateTime`/`DateTime?` | CLR default | `DateField` |
| `Time` | `DateTime`/`DateTime?` | CLR default | `DateField` |
| `DateTime` | `DateTime`/`DateTime?` | CLR default | `DateField` |

All three share the same editor; the difference is only whether it shows the date, the time, or
both. A field that needs a time zone adds `[ColumnShape(ColumnShape.TimestampWithTimeZone)]` to
the property as well.

### Choice

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Select` | `string` | `varchar(255)` | `SelectField` |
| `MultiSelect` | `List<string>` | JSON | `MultiSelectField` |
| `Radio` | `string` | `varchar(255)` | `RadioField` |
| `CheckboxGroup` | `List<string>` | JSON | `CheckboxGroupField` |
| `Tags` | `List<TagItem>` | JSON | `TagsField` |

`Select`, `MultiSelect`, `Radio`, and `CheckboxGroup` need `[CmsOptions]` to have anything to pick
from; `Tags` can pair with `[CmsOptions]` too, but it's optional and usually left free-form. How to
declare `[CmsOptions]` is in [Chapter 5: Defining Collections](05-collections.md).

On write, the only interfaces that actually check a value against the option list are
`MultiSelect`, `CheckboxGroup`, and `Select`/`Radio` used as `Repeater` child fields; `Select` and
`Radio` on their own accept any string.

### Structured JSON

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Json` | `string` (raw JSON text) | Long text | `JsonField` |
| `KeyValue` | `Dictionary<string,string>` | JSON | `KeyValueField` |
| `Repeater` | `List<TChild>` | JSON | `RepeaterField` |

`Json` is a `string` property holding raw JSON text; the API layer re-parses it on read and
re-serializes it on write. The real JSON columns come from the six structured interfaces —
`MultiSelect`, `CheckboxGroup`, `Tags`, `KeyValue`, `Files`, and `Repeater` — where SqlSugar
(de)serializes the whole CLR collection directly against an `IsJson = true` column; a `KeyValue`
key can't be blank.

`Repeater`'s `TChild` must be a class, not `string`, and must declare at least one `[CmsField]`;
violating either throws a startup `MetadataException` naming the field. Child fields are limited
to scalar interfaces — `Text`, `Textarea`, `Markdown`, `Code`, `Slug`, `Email`, `Url`, `Color`,
`Phone`, `Number`, `Slider`, `Rating`, `Boolean`, `Checkbox`, `Date`, `Time`, `DateTime`, `Select`,
`Radio` — and can't be set translatable. None of these are allowed:

- A nested `Repeater`
- `RichText`, `Json`, `KeyValue`
- `File`, `Image`, `Files`
- `Password`, `Hidden`, `Uuid`, `Divider`

### Media and files

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `File` | `Guid`/`Guid?` | CLR default | `FileField` |
| `Image` | `Guid`/`Guid?` | CLR default | `FileField` |
| `Files` | `List<Guid>` | JSON | `FilesField` |

`File` and `Image` share the same editor; `Image` adds an image preview. The ids `Files` stores
aren't checked for existence on write — an id pointing at an already-purged file is stored just
the same, and the admin picker can only fall back to showing the raw id.

### Display-only and identifiers

| Interface | CLR type | Database column | Admin editor |
|---|---|---|---|
| `Hidden` | Any | Follows the property type | `ReadonlyField` |
| `Divider` | Any | Follows the property type, or none | `DividerField` |
| `Uuid` | `Guid`/`Guid?` | CLR default | `ReadonlyField` |

The `Hidden` here is the interface itself, rendering a read-only display component — it's a
different thing from the next section's `[CmsField(Hidden = true)]`, which can sit on any
interface. `Divider` ignores the field's value; it's usually paired with
`[SugarColumn(IsIgnore = true)]` so it never becomes a real column at all. `Uuid` just displays a
GUID read-only.

### List columns and GraphQL exclusions

A collection's list view shows at most six columns, picked only from fields that are non-system and
non-hidden, and whose interface has a list-column formatter; `DefaultDisplayField` sorts first when
it qualifies, and the rest follow the field order in the API response.

Interfaces with no formatter, and so never shown in a list column, are `Password`, `Markdown`,
`Code`, `RichText`, `Divider`, `File`, `Image`, `Files`, `Hidden`, and `Uuid`. A collection built
entirely from these interfaces has an empty list view. The GraphQL schema separately excludes three
interfaces entirely: `Hidden`, `Divider`, and `Password`.

## `MaxLength` behavior

`MaxLength` governs only the admin input, counted in UTF-16 code units, and has nothing to do with
`[SugarColumn(Length = n)]` or an explicit `ColumnDataType`. A negative value, or one set on a
non-`string` property, throws a startup `MetadataException`. The effective `MaxLength` resolves in
this order:

1. Explicitly set and greater than zero — use the explicit value.
2. Otherwise, when the property is `string` and the interface is one of twelve short-string
   interfaces, fall back to 255: `Text`, `Slug`, `Email`, `Url`, `Password`, `Color`, `Phone`,
   `Select`, `MultiSelect`, `Radio`, `CheckboxGroup`, `Tags`.
3. Neither applies — `null`, meaning no length limit.

The admin binds the effective `MaxLength` directly to the native input's `maxlength` attribute, so
the 255 default takes effect on the frontend even when `MaxLength` was never written. This has
nothing to do with the database column's width — both happen to land on 255 only because
SqlSugar's own default for an unwidened `string` is also 255.

## Required, read-only, hidden, and system fields

`Required` always demands the field on create; on update it validates the merged entity, not the
request body itself — details are in [Chapter 5: Defining Collections](05-collections.md).

`RichText` content that sanitizes down to visually blank is converted to `null` before the
`Required` check runs, so a document that looks like it has content in the editor can still fail
the required check; `<img>` and `<hr>` both count as content.

On create, `Required` is reported first. On update, when the body also violates a length or
structural check, those errors are reported first instead.

A `ReadOnly` field can be read but not written on update — the update field-overlay logic skips
every `ReadOnly` and `IsSystem` field. Locking it on create as well requires the property to be
nullable: a non-nullable value type such as `[CmsField(ReadOnly = true)] public int Views` stores
whatever the client sends on create, and only becomes read-only afterward. The admin input is
disabled as well.

The scanner automatically turns the four audit fields into read-only system fields, picking their
interface by CLR type — `DateTime` for the timestamps, `Text` for the users. The framework stamps
all four on create; on update it only re-stamps `UpdatedAt`/`UpdatedBy`, and whatever the client
sends for any of the four is ignored. System fields still come back on read; they just don't appear
in the admin form or list columns.

`Hidden` (`[CmsField(Hidden = true)]`) has nothing to do with the interface — any interface can
carry it; the sample's `Article.InternalNote` is a `Hidden` `Text` field.

It makes the field disappear from `GET /api/schema`, the GraphQL schema, item responses, the
query DSL's known-field allowlist and searchable whitelist, and any externally returned revision
snapshot, and it also drops the field from the collection's `translation.fields` list — the raw
snapshot row still keeps the value.

It isn't an RBAC boundary: the update field-overlay logic never checks `Hidden` at all, so a
caller that already knows the field name can still write it; pairing it with `ReadOnly` is what
actually blocks the write.

### Cleanup on write

- An unrecognized key in the write body is silently dropped, no error — a misspelled field name
  still gets a successful response, only the value never gets stored.
- `MultiSelect`/`CheckboxGroup` dedupe and keep the first occurrence; `Tags` rejects blank values
  and drops duplicates.
- `Files` drops `Guid.Empty` and duplicates; `Repeater` drops any row that is entirely blank, but
  error messages still number rows starting at 1 in the order they were sent, including the dropped
  ones.

## Common pitfalls

**Writing long text fails on PostgreSQL with `22001 value too long`.** A `string` property with no
explicit column type, whose interface isn't one of the five long-text ones, keeps SqlSugar
CodeFirst's default `varchar(255)`; writing a value over 255 characters fails on PostgreSQL with
`22001 value too long for type character varying(255)`.

The fix is switching to one of those five interfaces, or writing
`[ColumnShape(ColumnShape.LongText)]` explicitly — not `[SugarColumn(ColumnDataType = "text")]`
directly: `ColumnShape` works on every backend.

**Adding `[ColumnShape]` on a JSON-interface field is rejected.** When a property's `[CmsField]`
interface is one of the six JSON interfaces, also carrying `[ColumnShape]` throws an
`InvalidOperationException` at startup naming the property and interface; a type inside
`InitTables`'s collection fails right at startup, while one outside it fails only the first time
that table is used.

The fix is removing `[ColumnShape]`: the JSON mapping already widens to long text and sets
`IsJson` on its own, and the shape alone gets neither. `[ColumnShape]` combined with the five
long-content interfaces (`Textarea`, `RichText`, `Markdown`, `Code`, `Json`) is legal and
unaffected.

When both `[ColumnShape]` and an explicit `ColumnDataType` sit on the same property,
`ColumnDataType` is ignored, with no warning at all.

**A `JsonElement` stops working once its `JsonDocument` is disposed.** Parsing a `Json` field's raw
text with `using var doc = JsonDocument.Parse(raw)` and then returning `doc.RootElement` outside the
`using` block throws `ObjectDisposedException` on the next read — a `JsonElement` is valid only
while the `JsonDocument` that produced it is still alive.

Using `JsonSerializer.Deserialize<JsonElement>(raw)` instead needs no `using`, and returns an
independent value that's safe to hold onto.

**A hand-declared `List<>` property needs `IsJson` written by hand.** A `List<>` property whose
`[CmsField]` interface falls outside `MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/`Files`/
`Repeater` gets no JSON mapping from the CodeFirst hook — SqlSugar can't map a list on its own, so
it needs `[SugarColumn(IsJson = true)]` written explicitly.

Once that's set, the bare-`IsJson` widening logic takes over and supplies the column type; when
`ColumnDataType` is also written explicitly (a PostgreSQL-native `jsonb`, say), the widening logic
respects it and leaves it alone — but that path has no test coverage, so its effect isn't
guaranteed.

The other way round, a property whose interface is already one of the six JSON interfaces gets
`IsJson` plus long text unconditionally from the hook, which never reads a `ColumnDataType` you
wrote — pinning `jsonb` on a `MultiSelect` field has no effect, and no warning either.

## Adding a custom field editor

The rest of this section swaps `Color`'s default `TextField` for a native color input. Every
field-editor component accepts the same three props and emits the same event: `field` (the resolved
field metadata), `modelValue` (the form's current value), and an optional `disabled`; it emits
`update:modelValue` back on change:

```ts
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
```

**1. Write the component** — `frontend/src/components/fields/ColorSwatchField.vue`:

```vue
<script setup lang="ts">
import { Input } from '@/components/ui/input'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

// The native color input uses the #rrggbb form; fall back to black while the field is still empty.
function onPick(e: Event): void {
  emit('update:modelValue', (e.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="flex items-center gap-2">
    <input
      type="color"
      :value="(modelValue as string) || '#000000'"
      :disabled="disabled"
      class="border-input h-9 w-12 shrink-0 cursor-pointer rounded-md border p-0.5"
      @input="onPick"
    />
    <Input
      :model-value="(modelValue as string)"
      :disabled="disabled"
      :maxlength="field.maxLength ?? undefined"
      @update:model-value="(v) => emit('update:modelValue', v)"
    />
  </div>
</template>
```

**2. Register it** — swap the `color` entry's component in
`frontend/src/lib/fieldTypes/registry.ts`; everything else (default value, parsing, serialization,
list-column formatter) still holds for a plain string field:

```ts
import ColorSwatchField from '../../components/fields/ColorSwatchField.vue'
// …
color: def({ component: ColorSwatchField, listColumn: asString }),
```

`FieldInput.vue` looks up the component from the registry by `field.interface`, so every `Color`
field across every collection switches over together — there's no per-collection wiring needed.

**3. See the result** — restart the frontend dev server, open any item with a `Color` field, and a
color swatch appears next to the input.

The dispatcher `FieldInput.vue` also forwards a fourth binding, `id`, passed down as a fallthrough
attribute — it isn't a prop any editor declares itself. An editor whose root element is a native
control picks up this `id`, matching the caller's `<label>`; an editor whose root element is a
wrapping `div` instead lands the `id` on that `div`, where it doesn't match the inner control, so it
has to handle labeling itself.

The `FieldInterface` union in `frontend/src/lib/fieldTypes/types.ts` is closed: adding a genuinely
new interface value — rather than swapping an existing interface's editor as above — also means
syncing the backend `FieldInterface` enum, the frontend union type, and every place in the
registry, which is beyond plain editor customization.

A schema contract test catches drift in both directions: the backend gaining a new member the
frontend hasn't caught up to, or the frontend keeping a stale entry, both fail the test.

That snapshot is regenerated by a single backend test run with `UPDATE_SCHEMA_SNAPSHOT=1` set, and
the flag must be cleared afterward, or later unrelated tests silently rewrite the snapshot instead
of checking it. Non-string fields such as numbers, booleans, and `Uuid` send `null` when left blank
(`number`, `slider`, `rating`, `boolean`, `checkbox`, `uuid`); text interfaces deliberately send an
empty string, so clearing a text field actually clears it.

## What's next

With an interface chosen, the next step is storing a separate value per language for a field — the
subject of [Chapter 7: Multilingual Content](07-i18n.md).
