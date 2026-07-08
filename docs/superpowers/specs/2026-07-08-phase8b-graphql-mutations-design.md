# Phase 8b.1 — GraphQL mutations (backbone) (design)

**Date:** 2026-07-08
**Status:** approved (brainstorm); pending implementation plan
**Scope:** the first mutation slice — a **strongly-typed** GraphQL write surface layered on the
Phase 8 read-only schema. 8b.1 covers **scalar own-fields + M2O relation foreign keys + optimistic
`version` + create/update/delete** end to end. Structured/multi-value/i18n field kinds are deferred
to **8b.2**; advanced read querying (cross-relation / deep filtering / multi-level nesting) is a
separate, parallel read-side slice (**8c**) and is **not** part of Phase 8b at all. This slice adds
to the `Struo.Api/GraphQl/` layer only — Domain / Application / Infrastructure are **untouched** and
gain **no new packages** (dependency rule §2). No backend write logic changes.

---

## 1. Goal & positioning

Phase 8 delivered a typed, introspectable, convention-generated **read** API: adding a
`[CmsCollection]` yields typed queries for free. Phase 8b extends the same promise to **writes** —
adding a `[CmsCollection]` should also yield typed `create`/`update`/`delete` mutations for free,
with no per-collection GraphQL code.

The entire write pipeline already exists and is live-verified through REST: `ItemService`
`CreateAsync(collection, JsonElement, ct)` / `UpdateAsync(collection, id, JsonElement, ct)` /
`DeleteAsync(collection, id, ct)` handle deserialization, per-field validation, RBAC
(`CanWrite` + `RequireSuperAdminForAdminOnly`), RichText sanitization, relation-FK / M2M /
translation sync, transactional atomicity (`InTransactionAsync`), and optimistic concurrency
(compare-and-swap on `version`). GraphQL mutations are a **new typed presentation layer over that
pipeline** — they do not re-implement writes.

### 1.1 Decisions (locked in the brainstorm)

- **Input typing: fully-typed per-collection input.** Each collection generates `XCreateInput` /
  `XUpdateInput`; the resolver converts the typed input back to a `JsonElement` and feeds the
  existing `ItemService`. Chosen over a generic `JSON`-scalar passthrough (which would throw away
  typing/introspection — the exact approach Phase 8 rejected for reads) and over a hybrid.
- **Return / error shape: bare node + existing error filter.** `createX(input): X` returns the
  created/updated node (the client selects fields); `deleteX(id): Boolean`. Failures flow through the
  Phase 8 `StruoErrorFilter` (stable `code` extension). No `payload { node, userErrors }` wrapper —
  consistent with the read side, minimal schema.
- **Slicing: backbone → structured (Option A).** 8b.1 = plumbing + scalar own-fields + M2O FK +
  `version` + delete (proves the highest-risk new pipeline end to end with a live gate); 8b.2 =
  i18n translations + M2M + File/Image/Files + multi-value + Json/KeyValue + Repeater inputs (more
  input-type shapes on the proven pipeline).

### 1.2 Non-goals (this slice)

- **Structured / multi-value / i18n input** — M2M, File/Image/Files, MultiSelect/CheckboxGroup/Tags,
  Json, KeyValue, Repeater, and typed `translations` input are all deferred to 8b.2. Their **fields
  are still writable via REST** in the meantime; they simply have no typed GraphQL input yet.
- **Advanced read querying** — cross-relation (dotted-path) filtering, nested-relation
  filter/sort/pagination, and multi-level (depth > 1) relation nesting remain deferred to a separate
  read-side slice (8c). Unrelated to mutations.
- **`payload`-wrapped user errors** — errors stay top-level via the error filter.
- **Delete concurrency token** — `ItemService.DeleteAsync` takes no `version`; `deleteX` therefore
  exposes none. (Optimistic delete is a possible future enhancement.)
- **Bulk mutations** — one node per mutation call (parity with REST single-item endpoints).

## 2. Layer & wiring

Mutations depend on HotChocolate/ASP.NET → a **web concern** → they live in `Struo.Api/GraphQl/`,
keeping Application/Infrastructure web-free (§2). New / changed files:

```
src/Struo.Api/GraphQl/
  StruoTypeModule.cs        // + BuildMutationExtension: registers create/update/delete per collection
  MutationResolvers.cs      // NEW — create/update/delete resolvers (mirrors CollectionResolvers)
  MutationInputMapper.cs    // NEW — typed input dict -> JsonElement bridge
  MutationInputBuilder.cs   // NEW — builds XCreateInput / XUpdateInput per collection
  GraphQlDataSource.cs      // + CreateAsync / UpdateAsync / DeleteAsync on IGraphQlDataSource
```

(File split is indicative; input-type building may fold into `CollectionSchemaBuilder`. Follow the
existing one-purpose-per-file convention.)

- **`IGraphQlDataSource`** gains three write methods delegating to `ItemService`:
  - `Task<IReadOnlyDictionary<string,object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct)`
  - `Task<IReadOnlyDictionary<string,object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct)`
  - `Task<bool> DeleteAsync(string collection, string id, CancellationToken ct)`
  Keeps the Api-owned read/write seam; no interface added to Application.
- **Mutation type extension**: `StruoTypeModule.CreateTypesAsync` adds an `ObjectTypeExtension` on
  `Mutation` with, per collection, `createX` / `updateX` / `deleteX` root fields, mirroring
  `BuildQueryExtension`. It also emits the `XCreateInput` / `XUpdateInput` input types.
- **Empty-schema guard**: GraphQL requires the root `Mutation` type (once present) to have ≥1 field.
  If zero writable collections are discovered, a minimal placeholder field keeps the schema valid
  (mirrors the read side's `_serviceCollections` guard). In practice `file`/`language`/`user` always
  exist, so this is a correctness backstop.
- **`Program.cs`**: no change — `app.MapStruoGraphQl()` already maps the endpoint after
  `CsrfProtectionMiddleware` + `PermissionResolutionMiddleware`; the same request scope resolves the
  scoped `ItemService` / `ICurrentPermissions`.

## 3. Input type generation & field mapping

Per collection, two input types (RuntimeType `IReadOnlyDictionary<string,object?>`, same pattern as
the existing `BuildFilterInput`, so the resolver receives a dict via
`ctx.ArgumentValue<IReadOnlyDictionary<string,object?>?>("input")`):

- **`XCreateInput`** — writable scalar own-fields + M2O foreign keys.
- **`XUpdateInput`** — the same fields **plus** `version: Long` (optimistic-lock echo). `id` is a
  separate mutation argument, not an input field.

### 3.1 Included / excluded fields

- **Included own-fields:** the **writable scalar interfaces** — Text / Textarea / RichText / Markdown
  / Code / Slug / Email / Url / Color / Phone / Time → `String`; Number / Slider / Rating →
  `Int`/`Long`/`Float` by CLR property type (same reflection as reads); Boolean / Checkbox →
  `Boolean`; Date / DateTime → `Date`/`DateTime`; Select / Radio → `String`; Uuid (non-id) → `String`.
  The exact interface list is fixed in the plan against `SchemaTypeMapper.ScalarSdl`.
- **Included relations:** M2O foreign key only, as `xxxId: ID` (e.g. `categoryId: ID`). O2M/M2M have
  no local FK to write in 8b.1 (M2M deferred to 8b.2).
- **Excluded:** `Hidden`, `ReadOnly`, `IsSystem` (id, `version` — handled specially — and audit
  columns), `Password` (Hidden anyway), `Divider` (presentational). Deferred-kind fields
  (MultiSelect/CheckboxGroup/Tags/Json/KeyValue/Files/Repeater/File/Image) are **omitted from the
  input** this slice (they are not yet mappable to typed input).
- **Exhaustiveness:** the input mapper reuses the read-side `SchemaTypeMapper` scalar mapping; an
  unmapped *included* interface is a startup fail-fast (asserted by a schema-generation test),
  mirroring the read side.

### 3.2 All input fields are nullable (required-ness validated server-side)

Every input field is nullable. Required-ness, the "default-locale translation required" rule, option
membership, MaxLength, etc. are **runtime metadata** enforced by `ItemService`, surfacing as
`BAD_USER_INPUT`. Reasons: (a) required-ness cannot be expressed in a flat create input (the
default-locale rule spans the translations object, deferred to 8b.2); (b) it matches the loosely-typed
REST contract exactly; (c) it avoids duplicating validation logic in the schema where it could drift.

### 3.3 Partial-update semantics

HotChocolate's input dict contains **only the keys the client supplied**. After conversion to
`JsonElement`, `ItemService.UpdateAsync`'s `bodyKeys` set therefore contains exactly the sent fields —
so the existing merge-update (overlay only sent, writable, non-system fields; preserve everything
else) works unchanged. Field-absent vs. explicit-null map to the same key-present/absent distinction
the REST path already relies on.

### 3.4 Mutation signatures

```graphql
type Mutation {
  createArticle(input: ArticleCreateInput!, locale: String): Article
  updateArticle(id: ID!, input: ArticleUpdateInput!, locale: String): Article
  deleteArticle(id: ID!): Boolean
  # …one triple per discovered collection
}
```

`locale` is optional and affects **only the returned node's projection/overlay** (§4), mirroring the
query `locale` argument (`explicit ?? collection-default ?? global-default`).

## 4. Input→JsonElement bridge & resolver flow

- **`MutationInputMapper`**: converts the input dict to a `JsonElement` via
  `JsonSerializer.SerializeToElement(dict, webOpts)` (the same `JsonSerializerDefaults.Web` options
  `ItemService` uses). Input values are already CLR primitives / strings; `ID` → string, `Long` →
  number, `Date`/`DateTime` → ISO string. An empty-string FK → null is handled by the existing
  `ItemService` deserialize coercion (the 7c Postgres fix). No manual per-type marshalling beyond what
  `System.Text.Json` produces.
- **create / update resolver**: (1) map input → `JsonElement`; (2) call
  `IGraphQlDataSource.CreateAsync` / `UpdateAsync`; (3) take the `id` from the write result; (4)
  **re-read** via `GetAsync(collection, id, deepSpec, locale)`, where `deepSpec` is built from the
  mutation's selection set (same `SelectionRelations` logic as `CollectionResolvers.ResolveSingle`).
  Returning the re-read node makes the mutation result behave **identically to a query result** —
  relations expand, `translations` present, projection/permission identical — rather than the bare
  `Project()` output of the write (which omits deep expansion). Cost: one extra `GetAsync` per
  mutation, negligible.
  - `update` returning `null` (row not found by id) → the mutation returns `null` (maps to REST 404).
- **delete resolver**: map nothing; call `DeleteAsync(collection, id, ct)`; return `true` (deleted) /
  `false` (not found) — parity with REST 204/404.

## 5. version / optimistic concurrency, RBAC & error mapping

- **Optimistic concurrency**: `XUpdateInput.version` flows into the body; `ItemService.UpdateAsync`
  sets `AuditableEntity.Version` from it and the repository's compare-and-swap
  (`WHERE id AND version = expected`) rejects a stale write with `ConcurrencyConflictException` →
  `StruoErrorFilter` → `CONFLICT`. Absent `version`, the existing fallback (freshly-loaded value, no
  protection) applies — backward-compatible for callers that don't track versions.
- **RBAC**: `ItemService` enforces `CanWrite(collection)` and `RequireSuperAdminForAdminOnly(meta)`.
  `AdminOnly` collections (e.g. `user`) expose mutations in the **global** schema, but a non-super-admin
  execution is denied → `FORBIDDEN`. Same "schema global, permission at execution time" model as reads.
- **Error mapping** (reuses the Phase 8 `StruoErrorFilter`, no new codes):

  | Domain exception | GraphQL `code` |
  |---|---|
  | `PermissionDeniedException` | `FORBIDDEN` (authenticated) / `UNAUTHENTICATED` (anonymous) |
  | `CollectionNotFoundException` | `NOT_FOUND` |
  | `QueryException` (validation: required / option / MaxLength / bad value) | `BAD_USER_INPUT` |
  | `ConcurrencyConflictException` / `RelationConflictException` | `CONFLICT` |
  | anything else | `INTERNAL_SERVER_ERROR` (masked, logged) |

## 6. CSRF (design note — zero code)

Mutations are `POST /graphql`. `CsrfProtectionMiddleware` already requires the `X-Struo-CSRF` header
for any non-safe method (POST included) that carries the session cookie and is not Bearer-authenticated;
`/graphql` is mapped after that middleware. So cookie-authenticated mutations are **already** CSRF-guarded
(as are cookie GraphQL reads, which are also POST); Bearer-token callers are exempt (no ambient credential).
No new CSRF work.

## 7. Testing strategy (TDD — failing test first, §17.2)

In the existing `tests/Struo.Tests` (SQLite for automated tests), gated finally on real Postgres.

1. **Schema generation (unit).** The `Mutation` type has `createX`/`updateX`/`deleteX` per collection;
   `XCreateInput`/`XUpdateInput` contain exactly the writable scalar own-fields + M2O FK; `version`
   present on update input only; Hidden/ReadOnly/System/Password/Divider and deferred-kind fields
   absent; all input fields nullable; unmapped included interface → startup fail-fast; SDL snapshot
   supported. `MutationInputMapper` dict→JsonElement mapping (ID/Long/Date/absent-key) unit-tested.
2. **Execution / integration (HotChocolate executor + SQLite).** `create` returns the node with the
   client's selected fields (incl. a selected M2O relation resolved via the re-read `deep`);
   `update` partial-merge (only sent fields change, unsent preserved); `update` unknown id → `null`;
   `delete` → `true`/`false`; `version` mismatch → `CONFLICT`; unreadable/unwritable collection →
   `FORBIDDEN`/`UNAUTHENTICATED`; missing required field → `BAD_USER_INPUT`; `AdminOnly` non-super-admin
   → `FORBIDDEN`; every error `code`.
3. **Live gate (real Postgres).** Representative mutations against live PG: create → returned node
   round-trips (CJK exact by code point); update partial-merge + M2O FK change; `version` concurrency
   conflict → `CONFLICT`; validation → `BAD_USER_INPUT`; permission → `FORBIDDEN`; delete → `true`
   then re-query `null`. Evidence recorded per the project live-gate convention (SQLite-green ≠
   Postgres-correct).

**Verification baseline continuity:** backend `dotnet build -warnaserror` clean + `dotnet test` all
green (449 + new GraphQL mutation tests). Frontend untouched (237).

## 8. Risks & open points

- **Input dict → JsonElement fidelity.** The main new surface. Mitigation: reuse `ItemService`'s web
  `JsonSerializerOptions`; unit-test the mapper against ID/Long/Date/absent-key; the live gate exercises
  real PG type coercion (Guid FK, timestamps).
- **Re-read-after-write locale.** The returned node overlays the optional `locale` arg (default-locale
  when omitted). Documented; matches query semantics. Translatable-field *writes* (the translations
  object) are 8b.2 — in 8b.1 a returned translatable field reflects the parent entity's own column only.
- **HotChocolate mutation registration via runtime `ObjectTypeExtension`.** Mirrors the proven
  `BuildQueryExtension` path; SDL snapshot + schema-gen tests pin the shape.
- **Deferred scope** (8b.2 structured/multi/i18n inputs; 8c advanced read querying) is recorded here so
  it isn't lost; each is a clean follow-up on the pipeline this slice proves.
