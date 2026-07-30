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

Neither is caught by `dotnet build`, `vue-tsc`, or either unit suite. Committing the snapshot turns
any change to the core content model into a reviewable diff, and splits enforcement across the two
CI jobs with no gap and no overlap:

| What went wrong | Which job fails |
|---|---|
| Core schema changed, snapshot not regenerated | `backend` — `CoreSchemaSnapshotTests` |
| Snapshot regenerated, but the SPA cannot render it | `frontend` — `frontend/tests/schemaContract.test.ts` |

### Regenerating

From the repository root:

```bash
UPDATE_SCHEMA_SNAPSHOT=1 dotnet test --filter CoreSchemaSnapshot
```

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
`frontend/src/lib/fieldTypes/types.ts` **and** giving it a component in
`frontend/src/lib/fieldTypes/registry.ts` — the frontend contract test enforces both.

### Ordering

Collections are sorted by `name` for a stable diff. **Field arrays are in wire order and must not be
sorted:** `selectListColumns` takes the first six eligible fields in array order, so reordering here
would make the frontend contract test assert against a column set production never produces.
