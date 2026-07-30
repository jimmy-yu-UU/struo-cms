# `schema/` — committed schema contract

## `core-collections.json`

The **core** collection metadata the admin SPA receives from `GET /api/schema`, captured verbatim in
wire shape (camelCase keys, camelCase enum strings, `Hidden` fields already stripped by
`SchemaService`). It covers only the seven `FrameworkEntityTypes` that carry `[CmsCollection]`
(`file`, `language`, `mediaFolder`, `permission`, `role`, `user`, `userRole`) — never the
`samples/Blog` collections, which a fork deletes.

**This file is machine-generated. Do not hand-edit it.**

### Why it is committed

The admin SPA mirrors these backend DTOs by hand
(`frontend/src/types/schema.ts`, `frontend/src/lib/fieldTypes/types.ts`), and drift is silent:

- an unknown `FieldInterface` falls back to a read-only renderer
  (`frontend/src/lib/fieldTypes/registry.ts`), so the field becomes uneditable without any error;
- a field whose interface has no list-column formatter is filtered out of the list view entirely
  (`frontend/src/lib/selectListColumns.ts`), so the column silently disappears.

For a field interface that one of the seven core collections actually uses, neither failure mode above
is caught by `dotnet build`, `vue-tsc`, or either unit suite on its own — closing that gap is the whole
reason this snapshot exists. Committing it turns any such change to the core content model into a
reviewable diff, split across the two CI jobs:

| What went wrong | Which job fails |
|---|---|
| Core schema changed, snapshot not regenerated | `backend` — `CoreSchemaSnapshotTests` |
| Snapshot regenerated, but the SPA cannot render it | `frontend` — `frontend/tests/schemaContract.test.ts` |

This gate is narrower than "the schema contract" might suggest, and it leaves real gaps: a
`FieldInterface` that no core collection uses (one added only for a fork's own collection, or for the
sample) is invisible to both jobs; relation shapes (`CmsRelation`/the `relations` array) are unguarded
entirely — nothing here asserts the admin SPA can render a particular relation kind, only that the
`relations` key is present; and a fork's own collections, once added, sit outside this snapshot by
definition. Treat it as covering exactly the seven core collections' field metadata, not the schema
surface as a whole.

### Regenerating

From the repository root (bash):

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
leave it set and a later, unrelated `dotnet test` silently **regenerates** the snapshot instead of
asserting against it, turning this gate off without any error or warning. Always pair the assignment
with the `Remove-Item` above.

Commit the result. Then run the frontend contract test, which is the half that decides whether the
admin SPA actually copes with the new shape:

```bash
cd frontend && pnpm test
```

### When to regenerate

Any change to a core `[CmsCollection]` entity or its `[CmsField]` attributes: adding, removing, or
renaming a field; changing a field's `Interface`, `Label`, `Required`, `Sortable`, `Searchable`,
`Translatable`, `MaxLength`, options, or sort order; or changing a collection's `Label`, `Icon`,
`Group`, `DefaultDisplayField`, `AdminOnly`, `Hidden`, soft-delete, or revisions status.

Adding a new member to `FieldInterface` also requires adding it to
`frontend/src/lib/fieldTypes/types.ts` and giving it a component in
`frontend/src/lib/fieldTypes/registry.ts`. Both halves are enforced, but by different mechanisms with
different scope. `registry.ts`'s `registry: Record<FieldInterface, FieldTypeDef>` requires an entry for
every member of the `FieldInterface` type, so `vue-tsc` (run via `pnpm build`) fails the moment the union
gains a member with no registry entry — regardless of whether any collection, core or fork, uses it.
`frontend/tests/schemaContract.test.ts` checks the same two spots at runtime (`vitest run`, no
type-checking involved) but only for interfaces a core collection actually uses: one `it()` fails if
such an interface is missing from `ALL_FIELD_INTERFACES`, another fails if it has no dedicated registry
entry. So for a core-used interface, both the missing-from-the-list and missing-from-the-registry
mistakes are caught twice, by independent means; for an interface only a fork's own collection (or the
sample) adopts, only the compiler's registry check still applies.

### Ordering

Collections are sorted by `name` for a stable diff. **Field arrays are in wire order and must not be
sorted:** `selectListColumns` takes the first six eligible fields in array order, so reordering here
would make the frontend contract test assert against a column set production never produces.
