# FE-R5 — Item Form (frontend redesign, slice 5)

> **Status:** design (brainstormed, approved 2026-07-20).
> **Slice of:** the **frontend admin redesign** (see FE-R0 §1 decomposition). This is **slice 5
> (FE-R5): the Item Form**, rebuilt on the FE-R0 design system + FE-R1 app shell in the established
> design language, re-skinning the existing `ItemFormView.vue` + `components/ItemForm.vue`.
> **Design-reference rule (user):** the prototype
> (`docs/struo-cms-frontend-design/struocms-admin-prototype.html`, `view-item` section) is a *visual*
> reference only — **no code is copied from it**; the screen is rebuilt from the design.

## 0. Summary

Re-skin the item create/edit form to the prototype's design language — a **page-head action bar**
(back + title + Delete/Save), the existing **shared → relations → translatable-Tabs** body re-styled,
plus two new touches: **per-locale translation-completeness dots** on the language tabs and a
**"translatable" badge** on translatable field labels — while preserving **every existing behaviour**
of the current view and form. No backend, API, route, or persistence change → **no `dotnet` gate, no
hard live Postgres gate**. A live smoke run is still recommended (§9) because the form relies on real
load / save / 409-recovery / soft-delete round-trips.

**Layout decision (approved): single-column re-skin (not the prototype's two-column grid).** StruoCMS
is schema-driven — the form renders *any* collection from `meta`, so the prototype's hardcoded
two-column blog layout (translatable body on the left; publish/slug/categories/featured-image cards on
the right) cannot be adopted literally. The prototype's side-column fields are simply an Article's
`shared` fields + relations, which the schema-driven form already classifies and renders. We therefore
keep the current **single vertical structure** (`shared` fields → `relations` → translatable Tabs) and
re-skin it, rather than splitting into main/side columns. A two-column layout may be revisited in a
later slice; it is explicitly out of scope here.

**Honest-data strategy (approved, consistent with FE-R3/FE-R4).** The prototype's item view assumes
concepts StruoCMS's backend does not have; none are adopted:

- **Publish status** (`草稿` / `已發布` / `已排程`) — StruoCMS has **no publish-status concept**. The
  prototype's status segment (SelectButton) and the "發布" side card are **dropped**.
- **Preview** — there is **no public preview endpoint**. The prototype's 預覽 button is **dropped**.
- **Locale-coverage aggregate** — not adopted as a backend feature; the per-locale **completeness
  dots are computed purely client-side** from the in-memory form model (see §3.3), so they add no
  backend dependency and stay honest.

## 1. Scope

**In scope**
- Rebuild `src/views/ItemFormView.vue` (assembly + string-extraction only): page-head action bar via
  `PageHeader`, re-skinned conflict banner, `ItemForm` embed — preserving all §2 behaviour.
- Re-skin `src/components/ItemForm.vue`: design-token field styling, translatable-field "translatable"
  badge, per-locale completeness dots on the Tabs; **remove its internal Cancel/Save actions row**
  (actions move to the page-head) while keeping `<form @submit.prevent>` so Enter still submits.
- Extend the **existing** `src/components/common/PageHeader.vue` with an optional `#lead` slot for a
  left-aligned back button. Backward-compatible: FE-R4's usage (no `#lead`) is unaffected.
- New pure helper `src/lib/localeCompleteness.ts` — `hasLocaleContent(fields, values) → boolean`.
- New `itemForm` i18n namespace (zh-TW default / en); extract the view's + form's hardcoded strings.

**Out of scope (YAGNI)**
- Any backend / API / route / persistence change.
- The prototype's **two-column** form grid + 320px side column (see §0).
- Publish-status segment / date / preview / featured-image side card (dropped — see §0).
- Autosave, draft states, slug auto-generation, revision UI (FE-R7 = 9c-fe).
- FE-R6 media library.
- Any change to field-type components (`fields/*`), `RelationInput`, or the item-form libs
  (`parseItemToForm` / `buildItemPayload` / `validateItem` / `formDirty` / `applyServerErrors`).

## 2. Preserved behaviour (must not regress)

The current view/form logic is correct and stays intact — only presentation and string-extraction
change. All of the following must continue to work exactly as today:

**`ItemFormView.vue`**
- **Load / init**: `Promise.all([schema.load(), langStore.load()])`; create → `blankItemForm`; edit →
  `itemsApi.get` with `deep: editableRelations` + default locale, mapped by `parseItemToForm`.
- **Route states**: loading / collection-not-found / item-not-found / no-create-permission notices.
- **Submit**: `validateItem` gate → `buildItemPayload` → create/update → re-baseline → route to list.
- **409 VERSION_CONFLICT recovery**: refresh version token WITHOUT discarding edits; arm conflict
  banner; "Reload latest" full-overwrites from the cached server copy. Other 409s / server `details`
  map to per-field errors + banner via `splitServerErrors`. (All unchanged — only the banner markup
  is re-skinned.)
- **Delete**: `deleteConfirm(deleteKindFor(meta))` via `ConfirmDialog`; on success re-baseline + route.
- **Dirty guards (FE-5 / NAV-1)**: `onBeforeRouteLeave` + `onBeforeRouteUpdate` (record switch only) +
  `beforeunload`; `guardLeave` resolves via ConfirmDialog accept/reject/onHide.
- **`defineExpose`** surface kept as-is (`init/onSubmit/onDelete/onCancel/reloadLatest/model/errors/
  serverError/notFound/loading/conflict`) so existing test hooks keep working; add exposed members
  only if a new test requires them.

**`ItemForm.vue`**
- **Field classification** via `splitFields(meta)` (shared vs translatable) + `meta.relations`.
- **Locale Tabs**: `activeLocale` model; the `watch(props.errors)` that jumps to the default-locale
  tab when validation errors arrive.
- **Per-field rendering** via `FieldInput` (all field types) and `RelationInput`; required `*` (default
  locale only for translatable), `helpText`, per-field `errors[f.name]` (default locale only for
  translatable).
- **`defineExpose({ activeLocale })`** kept.
- **Enter-to-submit** through the `<form @submit.prevent="emit('submit')">` wrapper.

## 3. Components

### 3.1 `PageHeader.vue` (extend the existing FE-R4 component)
- Add an optional **`#lead`** slot rendered before the `.titles` group (e.g. a back icon-button),
  inside a `.head-lead` wrapper that is only present when the slot is filled.
- No change to existing props (`title`, `caption?`) or the `#actions` slot. FE-R4's
  `CollectionListView` usage (no `#lead`) must render identically — asserted by keeping/extending the
  existing `PageHeader` test.

### 3.2 `ItemFormView.vue` (rebuild — assembly only)
Script logic is **carried over verbatim**; the template is rebuilt to assemble the new pieces:

```
<section class="item-form-view">
  <ConfirmDialog />
  <p v-if="loading" class="notice">{{ t('itemForm.loading') }}</p>
  <p v-else-if="!meta" class="notice">{{ t('itemForm.collectionNotFound') }}</p>
  <p v-else-if="notFound" class="notice">{{ t('itemForm.itemNotFound') }}</p>
  <p v-else-if="isCreate && !canWrite" class="notice">{{ t('itemForm.noCreatePermission') }}</p>
  <template v-else>
    <PageHeader :title="isCreate ? t('itemForm.new',{label:meta.label}) : t('itemForm.edit',{label:meta.label})">
      <template #lead>
        <Button text severity="secondary" icon="pi pi-chevron-left"
                :aria-label="t('itemForm.back')" @click="onCancel" />
      </template>
      <template #actions>
        <Button v-if="!isCreate && canDelete" :label="t('itemForm.delete')" severity="danger" @click="onDelete" />
        <Button v-if="canWrite" :label="t('itemForm.save')" :loading="submitting" @click="onSubmit" />
      </template>
    </PageHeader>

    <div v-if="conflict" class="conflict-banner" role="alert">
      <span class="conflict-text">{{ t('itemForm.conflictText') }}</span>
      <Button :label="t('itemForm.reloadLatest')" severity="secondary" size="small" @click="reloadLatest" />
    </div>

    <ItemForm :meta="meta" :item-id="id" :model="model" :locales="langStore.languages"
              :errors="errors" :server-error="serverError" :disabled="!canWrite"
              :submitting="submitting" @submit="onSubmit" />
  </template>
</section>
```

`onCancel` is unchanged (routes to the list, naturally intercepted by the dirty guard). The Save
button lives in the page-head and calls `onSubmit`; `ItemForm` no longer renders Save/Cancel but keeps
its `<form>` so Enter still emits `submit` → `onSubmit`.

### 3.3 Translation-completeness dots + `localeCompleteness.ts`
- New pure helper `src/lib/localeCompleteness.ts`:
  `hasLocaleContent(fields: FieldMeta[], values: Record<string, unknown>): boolean` — returns `true`
  iff at least one translatable field has a non-empty value for that locale. "Non-empty" = not
  `undefined`/`null`, not an empty string (after trim), not an empty array. Pure and independently
  tested — no store/DOM access.
- In `ItemForm.vue`, each locale Tab shows a dot: filled (`.dot`, success colour) when
  `hasLocaleContent(fields.translatable, model.translations[loc.code])`, else hollow (`.dot.off`).
- Dots render **only when `fields.translatable.length > 0` AND `locales.length > 1`** (a single-locale
  collection shows no dots — they would carry no information).

### 3.4 `ItemForm.vue` body (re-skin)
- **shared** fields: unchanged rendering, re-skinned `.field` (label + required `*` + `FieldInput` +
  `helpText` + `field-error`).
- **relations**: `<h3>{{ t('itemForm.relations') }}</h3>` + `RelationInput` per relation, unchanged.
- **translatable** Tabs: as today, plus the §3.3 dots and a **"translatable" badge**
  (`t('itemForm.translatableBadge')`) in a `.lbl-row` next to each translatable field label.
- The internal `.actions` row (Cancel/Save Buttons) is **removed**; `@cancel` emit is dropped from the
  component API (Cancel is now the page-head back button). `@submit` emit stays for Enter-to-submit.

## 4. i18n

New namespace **`itemForm`** in `src/locales/{zh-TW,en}/...` (per-slice namespace convention; `locales.test.ts` parity enforced). Keys (values illustrative, finalized in implementation):

| Key | zh-TW | en |
|---|---|---|
| `loading` | 載入中… | Loading… |
| `collectionNotFound` | 找不到集合 | Collection not found |
| `itemNotFound` | 找不到項目 | Item not found |
| `noCreatePermission` | 您沒有在此集合建立項目的權限 | You don't have permission to create items here |
| `new` | 新增 {label} | New {label} |
| `edit` | 編輯 {label} | Edit {label} |
| `delete` | 刪除 | Delete |
| `save` | 儲存 | Save |
| `back` | 返回列表 | Back to list |
| `relations` | 關聯 | Relations |
| `translatableBadge` | 可翻譯 | Translatable |
| `conflictText` | 此項目已被他人變更。檢視您的編輯後再次儲存以覆寫,或重新載入最新版本。 | This item was changed by someone else. Review your edits and save again to overwrite, or reload the latest version. |
| `reloadLatest` | 重新載入最新版本 | Reload latest |

## 5. Styling

- `.page-head` layout comes from `PageHeader`; the new `.head-lead` wrapper + back icon-button use
  FE-R0 layout tokens. Conflict banner re-skinned from hardcoded `--p-amber-*` to the FE-R0 **warn**
  status token layer (light/dark lockstep).
- Form `.field`, `.req`, `.help`, `.field-error`, `.lbl-row`, `.tr-badge`, and the locale-tab `.dot` /
  `.dot.off` use the OKLch layout + status tokens, matching the prototype's proportions — **not**
  copied CSS. The completeness dot's filled colour is the **success** status token.
- `InputText`, `Button`, `Tabs`, `ConfirmDialog`, and all field-type components keep their PrimeVue
  Aura preset skin (FE-R0). Component-scoped styles only; no global bleed.
- Back button uses **primeicons** (`pi pi-chevron-left`); dark/light flip in lockstep with the tokens.

## 6. Testing

- **Unit** — `localeCompleteness.hasLocaleContent`: empty values → false; one non-empty string → true;
  whitespace-only → false; empty array → false; non-empty array/number/boolean → true; no translatable
  fields → false.
- **Component**
  - `PageHeader`: existing cases still pass; new — `#lead` slot content renders inside `.head-lead`
    when provided, and `.head-lead` is absent when the slot is empty (FE-R4 no-`#lead` case).
  - `ItemForm`: translatable badge renders on translatable fields; dots render per §3.3 (filled vs
    hollow driven by model content; no dots when single locale or no translatable fields); internal
    Save/Cancel row is gone; the `<form>` submit still emits `submit`; `watch(errors)` still jumps to
    the default-locale tab.
- **View** — update `ItemFormView.test.ts` to drive the preserved §2 behaviour through the new
  template + `PageHeader`: create/edit load, not-found / no-permission notices, Save (header button →
  `onSubmit`) success path, 409 conflict banner + Reload latest, server-error field mapping, Delete
  confirm, dirty-guard prompts. Keep the `defineExpose`-based hooks working.
- **Gate** — `pnpm build` (vue-tsc typecheck) **and** `pnpm test` (vitest) both green. Per the FE
  lesson: vitest strips types → run `pnpm build` too. FE test count expected to grow.

## 7. Process

Subagent-driven (impl = Sonnet / review = Opus per task + Opus whole-branch review before merge),
spec → plan → execute → verify with an SDD ledger, `--no-ff` merge to `main`. Matches the FE-R0..R4
cadence. See [[workflow-model-and-cost-prefs]].

## 8. Live verification (recommended, not a hard gate)

Pure-frontend, so no DB gate — but a Playwright MCP smoke on real PG/backend is recommended because the
form depends on real load / save / 409-recovery / delete round-trips:

- Backend `ASPNETCORE_URLS=:5080` (Vite proxy default), bootstrap admin; `pnpm dev --host 127.0.0.1`.
- Use the `plugin_playwright` MCP (not the ECC bridge), logged-in.
- On a populated collection with translatable fields + relations (e.g. Article): page-head title +
  Delete/Save + back; switch locale tabs and confirm the completeness dot fills/empties as translatable
  fields gain/lose content; edit each field type (incl. RichText) and Save → returns to list; open an
  item → Delete → confirm; unsaved-edit leave prompt; dark/light lockstep; 0 console errors.

## 9. Risks / notes

- **Save relocation** — moving Save to the page-head means the header button drives `onSubmit`
  directly; the `<form>` wrapper is retained so Enter-to-submit is not lost. Verify both paths in tests.
- **PageHeader `#lead`** — the only edit to a shared FE-R4 component; kept additive/backward-compatible
  and covered by an explicit "no `#lead`" test so FE-R4 does not regress.
- **Completeness-dot semantics** — deliberately "any content" (option a), computed client-side; not a
  validity/required check and not a backend aggregate. Documented so it is not mistaken for publish
  readiness.
- **`ItemForm` API change** — dropping the `@cancel` emit is a breaking change to the component's
  contract; it has a single consumer (`ItemFormView`), updated in the same slice. No other callers.
- **No new deps** — reuse primeicons + existing PrimeVue components; no `pnpm add`.
