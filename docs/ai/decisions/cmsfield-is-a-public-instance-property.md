# `[CmsField]` is refused on a property the scan does not read

## Decision

The metadata scan refuses, with a `MetadataException`, a `[CmsField]` on a property that is static
or not public. `FieldDeclarationCheck.RequirePublicInstanceFields` runs for every type the scan
reads fields from: a collection class, a translation class, a `Repeater` child type, and the base
classes of each. A public property whose getter or setter is not public stays a field.

## Why

The scan reads fields with `GetProperties(Public | Instance)`, so a `[CmsField]` anywhere else
declares a field nobody gets: the collection starts, and the field is absent from the schema and
from every response. On a static property the ORM still maps a column, and creating an item fails.
Refusing the declaration at start names the property while the fork is still editing it.

The check lists the declared properties of each class in the hierarchy (`DeclaredProperties`), so it
sees the non-public ones without asking reflection for non-public members.

## Evidence

Measured on SQLite through the item service on 2026-11-26, at the commit before the check, each
shape next to a public `{ get; set; }` property as the control. "Scanned" is whether the field is in
the scanned metadata.

| Shape of the `[CmsField]` property | Column | Scanned | Create and read |
|---|---|---|---|
| `private` | none | no | the field is absent, no error |
| `internal` | none | no | the field is absent, no error |
| `public static` | yes | no | create threw `InvalidOperationException` |
| `public`, `{ private get; set; }` | yes | yes | as the control |
| `public`, `{ get; init; }` | yes | yes | as the control |
| `public`, `{ get; private set; }` | yes | yes | listed; the posted value is not stored |
| `public`, `{ get; }` | yes | yes | listed; the posted value is not stored |

The create on the static shape failed with `Sequence contains no elements`.

`FieldDeclarationCheckTests` pins the refusals (private, internal, protected and static on a
collection, a private field on a base class, an internal field on a translation class, a static
field on a repeater child) and that the four public shapes with a restricted accessor stay fields.

## Unknowns

- Measured on SQLite only, below the HTTP layer.
- A public `[CmsField]` property with a private setter, or with no setter, is listed as a field and
  a write to it is not stored. The check does not refuse it.
- An override that does not repeat `[CmsField]` is not a field: the attribute is not inherited, and
  the check looks at each declaration on its own. Not measured.
- A public `[CmsField]` property hidden by a non-public `new` member of a derived class was not
  measured.

## Referenced from

- `src/Struo.Infrastructure/Metadata/FieldDeclarationCheck.cs`
- `docs/ai/conventions.md`
