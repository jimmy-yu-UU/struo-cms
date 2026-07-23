# Admin UX Batch B — RBAC Management UX (問題 3 + 4) — Design Spec (2026-07-23)

## Background

From the user's 12-issue admin-UX report (2026-07-23), Batch B covers the two RBAC items:

- **問題 3**: Permission editing is unusable — grants must be created one row at a time in the
  `Permission` collection instead of being edited inside the Role.
- **問題 4**: Relation data shows bare Guids (e.g. `UserRole.UserId`/`RoleId`), and a user's roles
  should be visible/editable on the User form instead of via manual `UserRole` junction rows.
  Same root complaint as 問題 3.

Root cause (verified): `Permission.RoleId` and `UserRole.UserId`/`RoleId` are declared as
`FieldInterface.Text` — plain Guid text fields — even though the framework has full relation
support (`[Navigate]` + `[CmsRelation]`, proven end-to-end by `Article.Tags` M2M since Phase 7d).
No role-centric permission editor exists.

**Approved approach (user-selected: option C)**: permission matrix inside the Role form +
`User.Roles` M2M TagSelect + read-only effective-permissions preview on the User form +
`Permission`/`UserRole` hidden from the sidebar.

## Verified constraints

- `AdminOnly` gates **writes** only (`ItemService.RequireSuperAdminForAdminOnly`); reads remain
  governed by ordinary RBAC grants. So a Permission row granting *read* on an AdminOnly
  collection is meaningful; write/delete grants on AdminOnly collections are dead weight.
- Effective permissions are resolved fresh **once per request**
  (`PermissionResolutionMiddleware` → `IRolePermissionStore.LoadForUserAsync` →
  `PermissionResolver.Resolve`); no cache invalidation is needed when grants change.
- User **create** goes through the dedicated `POST /api/users` (password hashing); user **edit**
  goes through the generic items API, so an M2M navigation on `User` is editable on the edit form.
- `buildNav.ts` currently hardcodes hiding the `file` collection.
- Backend `CollectionMetadata` is serialized verbatim by `GET /api/schema`; adding a property
  automatically reaches the frontend (camelCase).

## Design

### 1. Backend

#### 1a. `User.Roles` M2M navigation (問題 4 core)

`src/Struo.Infrastructure/Identity/User.cs` gains:

```csharp
[Navigate(typeof(UserRole), nameof(UserRole.UserId), nameof(UserRole.RoleId))]
[CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
[SugarColumn(IsIgnore = true)]
public List<Role> Roles { get; set; } = [];
```

Same pattern as `Article.Tags`. The generic item form then renders a TagSelect showing role
**names**; writes go through the existing M2M set path. `AdminOnly` on `user` already ensures
only super-admins can write (no self-escalation). SSO JIT-provisioned users simply have an empty
tag list.

`UserRole` keeps its own `[CmsCollection]` (route stays valid for API/advanced use) but is
hidden from the nav (1b).

#### 1b. `Hidden` collection flag (core attribute)

- `CmsCollectionAttribute` gains `public bool Hidden { get; set; }` — XML-doc: hidden from the
  admin sidebar/nav; the collection stays fully reachable via REST/GraphQL and direct URLs.
- `CollectionMetadata` gains `public bool Hidden { get; init; }`; `MetadataScanner` copies it.
- Mark `Hidden = true` on: `Permission`, `UserRole` (implementation details behind the matrix /
  TagSelect), and `File` (media library is its admin surface; removes the `buildNav` hardcode).

#### 1c. Role permissions endpoints (問題 3 core)

New `RolesController` (`src/Struo.Api/Controllers/RolesController.cs`), super-admin only
(same `RequireAdmin()` guard pattern as `UsersController`), envelope-wrapped:

- `GET /api/roles/{id:guid}/permissions`
  → `data: [{ collection, canRead, canWrite, canDelete }]` — the role's existing Permission rows.
  404 (envelope) when the role doesn't exist.
- `PUT /api/roles/{id:guid}/permissions`
  Body: `[{ collection, canRead, canWrite, canDelete }]`. **Full-replace semantics in one
  transaction**: upsert incoming rows, delete the role's rows not in the payload; rows whose three
  flags are all `false` are treated as absent (deleted, never stored). Validation: 404 unknown
  role; 400 (envelope, `BAD_USER_INPUT`) for unknown collection names (validated against the
  metadata registry) or duplicate collection entries in the payload. Returns the stored set
  (same shape as GET).

Note: permission writes for AdminOnly collections are accepted by the API if sent (harmless —
backend ignores write grants on AdminOnly at enforcement), but the matrix UI never sends them
(disabled checkboxes, 2a). PUT does not need special AdminOnly filtering — YAGNI.

#### 1d. Effective-permissions endpoint (問題 4 preview)

- `GET /api/users/{id:guid}/effective-permissions` on `UsersController`, super-admin only.
  → `data: { isSuperAdmin: bool, permissions: { [collection]: { read, write, delete } } }`.
  Implementation **reuses** `IRolePermissionStore.LoadForUserAsync(id)` +
  `PermissionResolver.Resolve` — the preview is by construction identical to real authorization
  semantics (public-role floor for role-less users, super-admin short-circuit). 404 unknown user.

### 2. Frontend

#### 2a. `PermissionMatrix` component (Role edit page)

New `frontend/src/components/rbac/PermissionMatrix.vue`, mounted by `ItemFormView` **only when
`name === 'role'`, not in create mode, and the caller is super-admin** (a non-admin with a read
grant on `role` can open the form but the matrix endpoints would 403 — don't mount it for them;
same gating pattern as the FE-R7 revisions History button), below the generic form.

- Rows: all collections from the schema store (label + name), **including hidden ones**
  (permission on `file` etc. still matters for API consumers). Sorted by group then label.
- Columns: 讀 / 寫 / 刪 checkboxes.
- **AdminOnly collections**: row shown, Read checkbox active, Write/Delete checkboxes disabled
  with a tooltip (writes always require super-admin).
- **Role with `IsSuperAdmin = true`** (read from the loaded item): matrix replaced by a read-only
  notice ("super-admin has all permissions"). Reactive to the form field before save is NOT
  required — evaluate from the last-saved item state.
- Own Save button with its own dirty state (matrix saves via PUT, independent from the generic
  form's save; avoids mixing two APIs in one dirty guard). Toast on success/failure; 404/400
  surfaced via the existing `ApiError` path.
- Loading: GET on mount; unsaved-changes guard on route leave consistent with the form's existing
  dirty-guard UX.

#### 2b. User form additions

- Roles TagSelect: automatic once 1a lands (schema-driven).
- New `frontend/src/components/rbac/EffectivePermissionsPanel.vue`, mounted by `ItemFormView`
  only when `name === 'user'`, not in create mode, and the caller is super-admin (same rationale
  as the matrix): read-only table of the merged grants
  (or a super-admin notice), fed by `GET /api/users/{id}/effective-permissions`. Refetches after
  a successful form save (roles may have changed).

#### 2c. Nav filtering

`buildNav.ts`: filter `!c.hidden` instead of the hardcoded `c.name !== 'file'`.
`types/schema.ts` `CollectionMeta` gains `hidden: boolean`.

#### 2d. i18n

New `rbac` namespace in `en.ts` + `zh-TW.ts`: matrix headers (collection/read/write/delete),
save button, super-admin notices, AdminOnly tooltip, effective-permissions panel title, empty
state, load/save error messages.

### 3. Out of scope

- Upgrading `Permission.RoleId` / `UserRole.*` field declarations to relation dropdowns — the
  collections are hidden; the matrix and TagSelect are the canonical editors now.
- Audit-field (`CreatedBy`/`UpdatedBy`) name resolution — separate concern, already deferred.
- Any change to permission *enforcement* semantics.

## Testing

**Backend (xUnit + SQLite; live PG gate before merge):**
- `RolesController` permissions GET/PUT: super-admin gating (403), 404 unknown role, full-replace
  semantics (add/update/remove in one PUT), all-false rows dropped, unknown-collection 400,
  duplicate-collection 400, transactionality (failed PUT leaves prior rows intact).
- Effective-permissions endpoint: role-less user gets public floor, multi-role OR-merge,
  super-admin short-circuit, 404, non-admin caller 403.
- `User.Roles`: M2M read (names materialize) + set-write round-trip through the generic path;
  non-super-admin write rejected.
- `Hidden` flag: scanner copies attribute → metadata; schema endpoint serializes it.

**Frontend (vitest + `pnpm build`):**
- PermissionMatrix: renders rows from schema store; AdminOnly rows disable write/delete;
  super-admin role shows notice instead of matrix; save PUTs only non-all-false rows; dirty state.
- ItemFormView: matrix/panel mounted only for `role`/`user` respectively and only when editing.
- EffectivePermissionsPanel: renders merged grants; super-admin notice; refetch after save.
- buildNav: hidden collections excluded; `file` no longer special-cased.

**Batch-final live smoke (backend :5221 + Playwright MCP, live PG):**
create test role → tick grants in matrix → save → assign role to a test user via TagSelect →
effective-permissions panel shows the merge → log in as that user and verify nav/actions match →
clean up test data.

## Execution

Branch `admin-ux-batch-b` from `main`. Implementation subagents: **Sonnet 5**; review subagents:
**Fable 5** (user directive). TDD per task; conventional commits, no attribution footer.
