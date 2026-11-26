# An override repeats `[SugarColumn]`

## Decision

The metadata scan refuses, with a `MetadataException`, a property that overrides a base declaration
carrying `[SugarColumn]` and has no `[SugarColumn]` of its own. `ColumnOverrideCheck` runs on every
type the ORM maps and the scan reads: collection classes, translation classes and many-to-many
junction types. It looks at the public instance properties the type exposes, declared on it or
inherited, and names the nearest base declaration in the same virtual slot that carries the
attribute.

Not refused: an intermediate override without the attribute that the type overrides again with it; a
`new` member, which starts a slot of its own; a non-public property. On a collection or junction
with no marked primary key, an override of the marked key is left to the primary-key rule and its
message.

## Why

SqlSugar reads `[SugarColumn]` off the property's own declaration. The scan, the column checks and
the column conventions read it with attribute inheritance. On an override that drops the attribute
the two disagree about the column, and nothing reports it: the collection starts, and the difference
shows as a wrong column type, a write the database rejects, or a field the API treats as having no
column.

With the refusal in place, every declaration that starts carries its own attribute wherever a base
declaration has one, so a read with inheritance and a read without it return the same attribute. The
existing reads are left as they are.

## Evidence

Measured on SQLite through the item service on 2026-11-26, at the commit before the check. The base
class declares a virtual property with the attribute in the first column; the collection overrides
it with no `[SugarColumn]`. The control repeats the attribute.

| Base `[SugarColumn(...)]` | Column the ORM created | What the scan read | Effect |
|---|---|---|---|
| `IsIgnore = true` | a column, length 255 | no column | the field counts as having no column |
| `IsNullable = true`, `string` | `NOT NULL` | nullable | a create with `null` failed |
| `IsJson = true` | `varchar(1)` | JSON | column info only |
| `ColumnDataType = "text"` | `varchar(255)` | `text` | column info only |

In the first row a filter on the field answered `Unknown field`; in the second the database answered
`NOT NULL constraint failed`.

A covariant-return override is a slot of its own for reflection: neither SqlSugar nor a read with
inheritance finds the base attribute, so the two agree and the check has nothing to refuse.

`ColumnOverrideCheckTests` pins the refusals on a collection, a translation class and a junction, a
two-level chain, a generic base, an inherited override, and the accepted shapes.

## Unknowns

- Measured on SQLite only, below the HTTP layer. The column types of the last two rows were read
  from the ORM's column information, not from a write.
- That SqlSugar reads the attribute without inheritance was measured through its effects, not read
  from its source.

## Referenced from

- `src/Struo.Infrastructure/Metadata/ColumnOverrideCheck.cs`
- `docs/ai/conventions.md`
- `docs/ai/decisions/relation-key-and-primary-key-declarations.md`
