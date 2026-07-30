# `schema/` — committed schema contract

Two machine-generated files that pin what the admin SPA is expected to cope with. **Neither is
hand-edited** — see [Regenerating](#regenerating).

| File | Pins | Scope |
|---|---|---|
| `core-collections.json` | The core collection metadata the SPA receives from `GET /api/schema` | The seven core collections |
| `interfaces.json` | Every declared `FieldInterface` / `RelationInterface` member | All of them, used or not |

## Why they are committed

The admin SPA mirrors these backend DTOs and enums by hand
(`frontend/src/types/schema.ts`, `frontend/src/lib/fieldTypes/types.ts`,
`frontend/src/lib/relationInputKind.ts`), and every drift failure is silent:

- an unknown `FieldInterface` falls back to a read-only renderer
  (`frontend/src/lib/fieldTypes/registry.ts`), so the field becomes uneditable without any error;
- a field whose interface has no list-column formatter is filtered out of the list view entirely
  (`frontend/src/lib/selectListColumns.ts`), so the column silently disappears;
- an unmapped `RelationInterface` falls back to `'readonly'`
  (`frontend/src/lib/relationInputKind.ts`), so the relation silently becomes uneditable.

None of those is caught by `dotnet build`, `vue-tsc`, or either unit suite on its own. Committing the
snapshots turns any such change into a reviewable diff, split across the two CI jobs with no extra
workflow configuration — both halves ride inside the existing `dotnet test` and `pnpm test`:

| What went wrong | Which job fails |
|---|---|
| Backend schema or enums changed, snapshot not regenerated | `backend` — `CoreSchemaSnapshotTests` |
| Snapshot regenerated, but the SPA cannot handle it | `frontend` — `frontend/tests/schemaContract.test.ts` |

## `core-collections.json`

The **core** collection metadata, captured verbatim in wire shape (camelCase keys, camelCase enum
strings, `Hidden` fields already stripped by `SchemaService`). It covers only the seven
`FrameworkEntityTypes` that carry `[CmsCollection]` (`file`, `language`, `mediaFolder`, `permission`,
`role`, `user`, `userRole`) — never the `samples/Blog` collections, which a fork deletes.

The assertions driven by this file are **collection-scoped**: that every collection yields at least one
admin list column, that each `defaultDisplayField` survives into those columns, and that every DTO key
the frontend types require is present. Those checks see only these seven collections, so a fork's own
collections are outside their reach by definition.

### Ordering

Collections are sorted by `name` for a stable diff. **Field arrays are in wire order and must not be
sorted:** `selectListColumns` takes the first six eligible fields in array order, so reordering here
would make the frontend contract test assert against a column set production never produces.

## `interfaces.json`

Every member of `FieldInterface` and `RelationInterface`, in the same camelCase form the wire uses. The
names are produced with `JsonNamingPolicy.CamelCase` — the same policy `Program.cs` hands to
`JsonStringEnumConverter` — rather than a local re-implementation, so this file cannot drift from how
the API actually serializes an enum. Members appear in declaration order.

This file exists because `core-collections.json` can only ever reveal an interface that one of the seven
core collections *happens to use*, and new field types are normally introduced for content collections,
not for the framework's identity/file/language tables. Checking the enums directly makes the
interface-level half of this gate **unconditional**: a new member is caught the moment it is declared,
whether or not anything uses it yet.

What it enforces, via `frontend/tests/schemaContract.test.ts`:

- every declared `FieldInterface` member appears in `ALL_FIELD_INTERFACES`;
- every declared `FieldInterface` member has its own entry in `registry`;
- `ALL_FIELD_INTERFACES` lists nothing the backend no longer declares (catches a stale frontend entry
  left behind after a backend member is removed);
- every declared `RelationInterface` member has an entry in `relationInputKind`'s map
  (`MAPPED_RELATION_INTERFACES`), and that map lists nothing the backend no longer declares.

Both enums are therefore checked in both directions. The relation check tests map-key presence rather
than calling `relationInputKind()` and looking for `'readonly'`, for the same reason the field check
tests `registry` key presence rather than object identity: `'readonly'` is a legal `RelationInputKind`,
so a member deliberately mapped to it would be indistinguishable from one that fell through.

Note which of these the compiler already covers and which it does not. `registry` is declared
`Record<FieldInterface, FieldTypeDef>`, so `vue-tsc` (via `pnpm build`) independently fails the moment
the union gains a member with no registry entry. But `ALL_FIELD_INTERFACES` is typed
`readonly FieldInterface[]`, which a short list satisfies, and `relationInputKind`'s map is a plain
`Record<string, RelationInputKind>` — so for those two, this contract test is the **only** thing
standing between a forgotten mirror update and a silently uneditable field or relation.

## Regenerating

One command regenerates both files. From the repository root (bash):

```bash
UPDATE_SCHEMA_SNAPSHOT=1 dotnet test --filter CoreSchemaSnapshot
```

PowerShell:

```powershell
$env:UPDATE_SCHEMA_SNAPSHOT = 1
dotnet test --filter CoreSchemaSnapshot
Remove-Item Env:UPDATE_SCHEMA_SNAPSHOT
```

`$env:UPDATE_SCHEMA_SNAPSHOT = 1` persists for the rest of the PowerShell session unless you unset it —
leave it set and a later, unrelated `dotnet test` silently **regenerates** the snapshots instead of
asserting against them, turning this gate off without any error or warning. Always pair the assignment
with the `Remove-Item` above.

Commit the result. Then run the frontend contract test, which is the half that decides whether the
admin SPA actually copes with the new shape:

```bash
cd frontend && pnpm test
```

## When to regenerate

**`core-collections.json`** — any change to a core `[CmsCollection]` entity or its `[CmsField]`
attributes: adding, removing, or renaming a field; changing a field's `Interface`, `Label`, `Required`,
`Sortable`, `Searchable`, `Translatable`, `MaxLength`, options, or sort order; or changing a
collection's `Label`, `Icon`, `Group`, `DefaultDisplayField`, `AdminOnly`, `Hidden`, soft-delete, or
revisions status.

**`interfaces.json`** — any change to the `FieldInterface` or `RelationInterface` enums.

Adding a `FieldInterface` member also requires `SchemaTypeMapper` (or GraphQL schema build throws
during host startup, surfacing as a misleading `ObjectDisposedException` long before this gate speaks),
plus `frontend/src/lib/fieldTypes/types.ts` and a component in
`frontend/src/lib/fieldTypes/registry.ts`. Adding a `RelationInterface` member requires a matching entry
in `frontend/src/lib/relationInputKind.ts`. `AGENTS.md`'s field-type playbook has the full ordered
checklist.

## Scope, stated plainly

Interface-level coverage is unconditional: no `FieldInterface` or `RelationInterface` member can be
added without the frontend mirrors being updated. Collection-level coverage is not: the list-column,
`defaultDisplayField`, and DTO-key assertions apply to the seven core collections only. A fork that adds
its own collections gets the interface guarantees for free and should extend `core-collections.json`'s
generator if it wants the collection-level ones too.
