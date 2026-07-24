# Admin UX Batch B.1 — RBAC Follow-up Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the three live-use complaints against Batch B: double leave-guard dialog (options "do nothing"), stale effective-permissions preview, and no matrix on role create.

**Architecture:** One backend change (`?roles=` hypothetical query on the effective-permissions endpoint via a new `IRolePermissionStore.LoadForRolesAsync`) and three frontend changes (unified leave guard owned by ItemFormView; create-mode matrix buffered and submitted with the form Save; panel live-refetch driven by the TagSelect selection). Spec: `docs/superpowers/specs/2026-07-24-admin-ux-batch-b1-rbac-followup-design.md`.

**Tech Stack:** .NET 10 / SqlSugar / xUnit (SQLite tests); Vue 3 + TS + PrimeVue + vitest (pnpm).

## Global Constraints

- Branch `admin-ux-batch-b1` from `main` (created before Task 1).
- Implementation subagents: **Sonnet**; reviews: **Fable 5**.
- Gates: `dotnet test` (repo root) green; `pnpm test` + `pnpm build` (frontend/) green.
- TDD per task; conventional commits, NO attribution footer.
- Do NOT touch `frontend/vite.config.ts` or `docs/struo-cms-frontend-design/`.
- i18n: new keys in BOTH `frontend/src/locales/en.ts` and `zh-TW.ts`.
- All tasks touch overlapping files — run tasks STRICTLY sequentially 1→4.

---

### Task 1: Backend — hypothetical `?roles=` preview query

**Files:**
- Modify: `src/Struo.Application/Security/IRolePermissionStore.cs`
- Modify: `src/Struo.Infrastructure/Identity/SqlSugarRolePermissionStore.cs`
- Modify: `src/Struo.Api/Controllers/UsersController.cs` (the `GetEffectivePermissions` action)
- Modify: `tests/Struo.Tests/Files/FileAccessPolicyTests.cs` (the `UnusedRolePermissionStore` fake must implement the new interface member — mirror its existing throwing/unused style)
- Test: `tests/Struo.Tests/Api/EffectivePermissionsEndpointTests.cs` (extend)

**Interfaces:**
- Produces: `Task<RolePermissionData> LoadForRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct = default)` — loads exactly the given roles + their permission rows; EMPTY list falls back to the `public` role (same floor semantics as a role-less user in `LoadForUserAsync`).
- Endpoint contract: `GET /api/users/{id:guid}/effective-permissions?roles=<guid>,<guid>` — with `roles` present (even empty), the preview uses the hypothetical set; unknown role id → 400 `BAD_USER_INPUT` listing the ids; malformed GUID → 400. Absent param → stored-roles behavior unchanged.

- [ ] **Step 1: Write the failing tests**

Extend `tests/Struo.Tests/Api/EffectivePermissionsEndpointTests.cs` (reuse its existing `CreateRoleWithGrantAsync`/`CreateUserWithRolesAsync` helpers):

```csharp
    // B.1 #2: the User form previews the CURRENT TagSelect selection before saving.
    [Fact]
    public async Task Roles_query_overrides_the_stored_role_set()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var storedRole = await CreateRoleWithGrantAsync(admin, "article", read: true, write: false);
        var hypoRole = await CreateRoleWithGrantAsync(admin, "tag", read: true, write: true);
        var userId = await CreateUserWithRolesAsync(admin, [storedRole]);

        var data = (await admin.GetFromJsonAsync<JsonElement>(
                $"/api/users/{userId}/effective-permissions?roles={hypoRole}"))
            .GetProperty("data");
        var perms = data.GetProperty("permissions");
        perms.TryGetProperty("article", out _).Should().BeFalse("stored roles must be ignored");
        perms.GetProperty("tag").GetProperty("write").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Empty_roles_query_previews_the_public_floor()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var role = await CreateRoleWithGrantAsync(admin, "article", read: true, write: true);
        var userId = await CreateUserWithRolesAsync(admin, [role]);

        var data = (await admin.GetFromJsonAsync<JsonElement>(
                $"/api/users/{userId}/effective-permissions?roles="))
            .GetProperty("data");
        data.GetProperty("isSuperAdmin").GetBoolean().Should().BeFalse();
        foreach (var p in data.GetProperty("permissions").EnumerateObject())
        {
            p.Value.GetProperty("write").GetBoolean().Should().BeFalse("public floor is read-only");
        }
    }

    [Fact]
    public async Task Unknown_or_malformed_role_id_is_400()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var userId = await CreateUserWithRolesAsync(admin, []);
        (await admin.GetAsync($"/api/users/{userId}/effective-permissions?roles={Guid.NewGuid()}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"/api/users/{userId}/effective-permissions?roles=not-a-guid"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

`dotnet test --filter "FullyQualifiedName~EffectivePermissionsEndpointTests"`
Expected: new tests FAIL (`roles` query ignored → stored grants returned; 200 instead of 400).

- [ ] **Step 3: Implement**

`IRolePermissionStore.cs` — add to the interface:

```csharp
    /// <summary>
    /// Loads RBAC data for a HYPOTHETICAL role set (User-form preview of an unsaved TagSelect
    /// selection). An empty list follows the same public-role floor as a role-less user.
    /// Ids not matching an existing role are simply absent from the result — the caller decides
    /// whether that is an error.
    /// </summary>
    Task<RolePermissionData> LoadForRolesAsync(IReadOnlyList<Guid> roleIds, CancellationToken ct = default);
```

`SqlSugarRolePermissionStore.cs` — implement (mirrors the second half of `LoadForUserAsync`):

```csharp
    public async Task<RolePermissionData> LoadForRolesAsync(
        IReadOnlyList<Guid> roleIds, CancellationToken ct = default)
    {
        List<Role> roles;
        if (roleIds.Count == 0)
        {
            // Public floor: an empty hypothetical set previews what a role-less user would get.
            roles = await db.Queryable<Role>().Where(r => r.Name == "public").ToListAsync(ct);
        }
        else
        {
            var ids = roleIds.ToList();
            roles = await db.Queryable<Role>().Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        }

        if (roles.Count == 0)
            return new RolePermissionData([], []);

        var loadedIds = roles.Select(r => r.Id).ToList();
        var perms = await db.Queryable<Permission>()
            .Where(p => loadedIds.Contains(p.RoleId))
            .ToListAsync(ct);

        return new RolePermissionData(
            roles.Select(r => new RoleRow(r.Id, r.Name, r.IsSuperAdmin)).ToList(),
            perms.Select(p => new PermissionRow(p.RoleId, p.Collection, p.CanRead, p.CanWrite, p.CanDelete)).ToList());
    }
```

`UsersController.GetEffectivePermissions` — add `[FromQuery] string? roles` and branch before the resolve:

```csharp
        RolePermissionData data;
        if (roles is null)
        {
            data = await rolePermissions.LoadForUserAsync(id, ct);
        }
        else
        {
            // B.1 #2: hypothetical preview of an unsaved role selection. Empty -> public floor.
            var parts = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var ids = new List<Guid>(parts.Length);
            foreach (var p in parts)
            {
                if (!Guid.TryParse(p, out var rid))
                    return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                        $"Malformed role id: {p}");
                ids.Add(rid);
            }
            data = await rolePermissions.LoadForRolesAsync(ids, ct);
            // Unknown ids silently shrinking the preview would show grants that don't match the
            // selection — reject instead. (Empty request -> public role loads; roles named
            // 'public' are still a real match, so only compare when ids were requested.)
            if (ids.Count > 0)
            {
                var loaded = data.Roles.Select(r => r.Id).ToHashSet();
                var missing = ids.Where(i => !loaded.Contains(i)).ToList();
                if (missing.Count > 0)
                    return ApiResults.Fail(StatusCodes.Status400BadRequest, ErrorCodes.BadUserInput,
                        $"Unknown role ids: {string.Join(", ", missing)}");
            }
        }
        var eff = PermissionResolver.Resolve(data);
```

(then the existing projection over `eff` continues unchanged).

`FileAccessPolicyTests.cs` — extend the `UnusedRolePermissionStore` fake with the new member in its existing style (throwing "should not be called").

- [ ] **Step 4: Run the new tests, then the full backend suite**

`dotnet test --filter "FullyQualifiedName~EffectivePermissionsEndpointTests"` → PASS.
`dotnet test` → all green.

- [ ] **Step 5: Commit**

```bash
git add src/Struo.Application/Security/IRolePermissionStore.cs src/Struo.Infrastructure/Identity/SqlSugarRolePermissionStore.cs src/Struo.Api/Controllers/UsersController.cs tests/Struo.Tests/Api/EffectivePermissionsEndpointTests.cs tests/Struo.Tests/Files/FileAccessPolicyTests.cs
git commit -m "feat(rbac): effective-permissions ?roles= hypothetical preview query"
```

---

### Task 2: Frontend — unified leave guard + form Save flushes the matrix

**Files:**
- Modify: `frontend/src/components/rbac/PermissionMatrix.vue`
- Modify: `frontend/src/views/ItemFormView.vue`
- Test: `frontend/src/components/rbac/PermissionMatrix.test.ts`, `frontend/src/views/ItemFormView.test.ts` (extend)

**Interfaces:**
- `PermissionMatrix` LOSES its route guard (remove `onBeforeRouteLeave`, `useConfirm`, `unsavedConfirm` import — and the `vue-router` mock in its test file becomes unnecessary).
- `save()` now returns `Promise<boolean>` (true on success; false on failure after its own error toast). `defineExpose` unchanged otherwise (`toggle, save, dirty, load` — Task 3 adds more).
- `ItemFormView.guardLeave()` checks `isDirty(baseline.value, model) || (matrix.value?.dirty ?? false)`; `onBeforeUnload` checks the same combined condition. Template ref: `const matrix = ref<InstanceType<typeof PermissionMatrix> | null>(null)` bound via `ref="matrix"` on `<PermissionMatrix>`.
- `onSubmit` success path (role, edit mode): if `matrix.value?.dirty`, `const ok = await matrix.value.save(); if (!ok) return` (before capture-baseline/navigation) — one Save saves everything; a failed matrix save keeps the user on the page.

- [ ] **Step 1: Write the failing tests**

`PermissionMatrix.test.ts`:
- Remove the `vi.mock('vue-router', ...)` line and add a regression test that the component registers NO route guard (e.g. import the real `vue-router` is not needed — simplest is asserting `save()` return value):

```ts
  it('save resolves true on success and false on failure', async () => {
    seedSchema()
    vi.mocked(rbacApi.putRolePermissions).mockResolvedValue([])
    const w = mountMatrix()
    await flushPromises()
    const vm: any = w.vm
    vm.toggle('article', 'write', true)
    await expect(vm.save()).resolves.toBe(true)
    vi.mocked(rbacApi.putRolePermissions).mockRejectedValue(new Error('boom'))
    vm.toggle('article', 'delete', true)
    await expect(vm.save()).resolves.toBe(false)
  })
```

`ItemFormView.test.ts` (adapt to the file's helpers; key assertions):

```ts
  it('leave guard fires once and covers a dirty matrix (unified guard)', async () => {
    // mount role edit as super-admin; make ONLY the matrix dirty via its exposed toggle;
    // invoke the view's exposed guardLeave-backed route hook (or call guardLeave via vm if exposed);
    // assert confirm.require was called EXACTLY once.
  })

  it('form Save flushes a dirty matrix and blocks navigation when matrix save fails', async () => {
    // role edit; matrix dirty; matrix save mocked to fail (rbacApi.putRolePermissions rejects);
    // run onSubmit; assert router.push NOT called; then resolve the PUT and assert push happens.
  })
```

Write these as REAL tests against the file's mount/seed helpers (stub `rbacApi` like the existing rbac tests; `confirm.require` is observable by spying on the ConfirmationService — follow how existing dirty-guard tests in this file assert the confirm flow; if none exist, expose what's needed via the view's `defineExpose` rather than skipping the assertion).

- [ ] **Step 2: Run to verify RED**, **Step 3: Implement** (per the Interfaces block above — in `PermissionMatrix.vue` delete the guard block + imports and change `save` to `return true / return false` in the try/catch; in `ItemFormView.vue` add the `matrix` ref, extend `guardLeave` + `onBeforeUnload`, and insert the flush into `onSubmit`), **Step 4: full `pnpm test` + `pnpm build` green**, **Step 5: Commit:**

```bash
git add frontend/src/components/rbac/PermissionMatrix.vue frontend/src/views/ItemFormView.vue frontend/src/components/rbac/PermissionMatrix.test.ts frontend/src/views/ItemFormView.test.ts
git commit -m "fix(admin-ui): single unified leave guard; form Save flushes a dirty permission matrix"
```

---

### Task 3: Frontend — create-mode matrix, submitted with the form Save

**Files:**
- Modify: `frontend/src/components/rbac/PermissionMatrix.vue`
- Modify: `frontend/src/views/ItemFormView.vue`
- Modify: `frontend/src/locales/en.ts`, `frontend/src/locales/zh-TW.ts`
- Test: `frontend/src/components/rbac/PermissionMatrix.test.ts`, `frontend/src/views/ItemFormView.test.ts` (extend)

**Interfaces:**
- `PermissionMatrix` new prop `createMode?: boolean` (default false). When true: `onMounted` skips `load()` (grants `{}`, baseline `'{}'`, `loading=false`), the "Save permissions" button is NOT rendered. New exposed `currentEntries(): RolePermissionEntry[]` — the same non-all-false fold `save()` uses (extract a shared helper `foldEntries()` so save and currentEntries cannot drift).
- `ItemFormView`: `showMatrix` drops `!isCreate` → `name === ROLE_COLLECTION && auth.user?.isSuperAdmin === true`; `<PermissionMatrix :role-id="idStr" :create-mode="isCreate" ...>`. In `onSubmit`'s CREATE path for role: capture `const created = await itemsApi.create(...)` (it returns the created item), read `created.id`, and if `matrix.value?.currentEntries().length` → `await rbacApi.putRolePermissions(String(created.id), entries)`. On grants-PUT failure: toast `rbac.grantsSaveFailedAfterCreate` (severity warn, life 6000) and `router.push` to the new role's EDIT page instead of the list; on success follow the normal post-create navigation.
- i18n keys (both locales): `grantsSaveFailedAfterCreate` — en: `'The role was created, but saving its permissions failed — retry from the role\'s edit page.'`; zh-TW: `'角色已建立，但權限儲存失敗——請在該角色的編輯頁重試。'`

- [ ] **Step 1: failing tests** — matrix: `createMode` renders table without GET and without the save button; `currentEntries()` returns only non-all-false rows. ItemFormView: role create as super-admin mounts the matrix; submitting a create with matrix entries calls `rbacApi.putRolePermissions` with the id returned by `itemsApi.create`; grants-PUT rejection → toast + push to the edit route (assert push target), no unhandled rejection.
- [ ] **Step 2: RED**, **Step 3: implement**, **Step 4: `pnpm test` + `pnpm build` green**, **Step 5: Commit:**

```bash
git add frontend/src/components/rbac/PermissionMatrix.vue frontend/src/views/ItemFormView.vue frontend/src/locales/en.ts frontend/src/locales/zh-TW.ts frontend/src/components/rbac/PermissionMatrix.test.ts frontend/src/views/ItemFormView.test.ts
git commit -m "feat(admin-ui): permission matrix on role create — grants buffered and saved with the form"
```

---

### Task 4: Frontend — live effective-permissions preview

**Files:**
- Modify: `frontend/src/api/rbacApi.ts` + `frontend/src/api/rbacApi.test.ts`
- Modify: `frontend/src/components/rbac/EffectivePermissionsPanel.vue` + its test
- Modify: `frontend/src/views/ItemFormView.vue` + its test
- Modify: `frontend/src/locales/en.ts`, `frontend/src/locales/zh-TW.ts`

**Interfaces:**
- `rbacApi.getEffectivePermissions(userId: string, roleIds?: string[])` — when `roleIds` is provided (INCLUDING an empty array) appends `?roles=<csv>` (empty array → `?roles=`).
- `EffectivePermissionsPanel` new prop `roleIds?: string[]`. Watcher (deep-safe: watch a `computed(() => (props.roleIds ?? []).join(','))`) triggers `reload()` debounced 300 ms; `reload()` passes `props.roleIds` through when the prop is defined. Template adds a hint line under the title: `<p class="hint">{{ t('rbac.effectivePreviewHint') }}</p>` (shown whenever `roleIds` is provided).
- `ItemFormView`: passes `:role-ids="selectedRoleIds"` where `const selectedRoleIds = computed(() => (model.relations.roles as string[] | undefined) ?? [])`. REMOVE the dead post-save `void effPanel.value?.reload()` line and its no-op comment block (superseded by the live preview); keep the `effPanel` ref only if still needed — if nothing else uses it, remove it and the template `ref`.
- i18n (both locales): `effectivePreviewHint` — en: `'Preview follows the currently selected roles, including unsaved changes.'`; zh-TW: `'預覽依目前選擇的角色計算，包含尚未儲存的變更。'`
- Debounce: use `setTimeout`/`clearTimeout` inline (no new dependency); expose nothing new.

- [ ] **Step 1: failing tests** — rbacApi: roleIds → `?roles=a,b`; empty array → `?roles=`; omitted → no query. Panel: changing `roleIds` prop triggers a refetch with the new csv after the debounce (use `vi.useFakeTimers()`); hint renders when prop provided. ItemFormView: panel receives the current selection; the dead reload call is gone (delete the now-obsolete "saving reloads it" assertion; replace with one asserting the panel gets `role-ids`).
- [ ] **Step 2: RED**, **Step 3: implement**, **Step 4: `pnpm test` + `pnpm build` green**, **Step 5: Commit:**

```bash
git add frontend/src/api/rbacApi.ts frontend/src/api/rbacApi.test.ts frontend/src/components/rbac/EffectivePermissionsPanel.vue frontend/src/components/rbac/EffectivePermissionsPanel.test.ts frontend/src/views/ItemFormView.vue frontend/src/views/ItemFormView.test.ts frontend/src/locales/en.ts frontend/src/locales/zh-TW.ts
git commit -m "feat(admin-ui): effective-permissions preview follows the unsaved role selection live"
```

---

## Batch-final verification (orchestrator)

1. `dotnet test` + `cd frontend && pnpm test && pnpm build` — green.
2. Live smoke (backend :5221 `dotnet run`, vite `--host 127.0.0.1`, Playwright MCP, admin@admin.com):
   - **#1**: role edit → dirty BOTH name and matrix → leave → exactly ONE dialog → Yes leaves immediately; No stays. Form Save with dirty matrix saves both (verify grants via GET).
   - **#3**: New Role → matrix visible and editable, no its-own save button → fill name + tick grants → Save → role created AND grants persisted (GET verify), lands per plan.
   - **#2**: user edit → change Roles TagSelect (no save) → panel updates within ~1s and shows the hint; empty selection shows public floor.
   - Clean up test data.
3. `pnpm playwright test` e2e (E2E_API=:5221, E2E_EMAIL/E2E_PASSWORD=dev admin, limiter env off, --workers=1, fresh E2E_STAMP) — 17/17.
4. Merge decision per superpowers:finishing-a-development-branch.
