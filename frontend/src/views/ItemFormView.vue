<script setup lang="ts">
import { reactive, ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter, onBeforeRouteLeave, onBeforeRouteUpdate } from 'vue-router'
import { useConfirm } from '@/composables/useConfirm'
import { useToast } from '@/composables/useToast'
import { useI18n } from 'vue-i18n'
import { ChevronLeft, History } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import ItemForm from '../components/ItemForm.vue'
import PageHeader from '../components/common/PageHeader.vue'
import RevisionHistoryDrawer from '../components/revisions/RevisionHistoryDrawer.vue'
import PermissionMatrix from '../components/rbac/PermissionMatrix.vue'
import EffectivePermissionsPanel from '../components/rbac/EffectivePermissionsPanel.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'
import { rbacApi } from '../api/rbacApi'
import { ApiError } from '../api/apiClient'
import { blankItemForm, parseItemToForm } from '../lib/parseItemToForm'
import { buildItemPayload } from '../lib/buildItemPayload'
import { validateItem } from '../lib/validateItem'
import { splitServerErrors } from '../lib/applyServerErrors'
import { relationInputKind } from '../lib/relationInputKind'
import { deleteKindFor, deleteConfirm } from '../lib/deleteAction'
import { snapshotModel, isDirty, unsavedConfirm } from '../lib/formDirty'
import { LANGUAGE_COLLECTION, ROLE_COLLECTION, USER_COLLECTION } from '../lib/frameworkCollections'
import type { FormModel } from '../types/itemForm'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()
const langStore = useLanguageStore()
const confirm = useConfirm()
const toast = useToast()
const { t } = useI18n()

const name = computed(() => route.params.name as string)
const id = computed(() => (route.params.id as string | undefined) ?? undefined)
const idStr = computed(() => id.value ?? '')
const isCreate = computed(() => id.value === undefined)
const meta = computed(() => schema.get(name.value))
const editableRelations = computed(() =>
  (meta.value?.relations ?? [])
    .filter((r) => ['dropdown', 'tagSelect', 'treeSelect'].includes(relationInputKind(r.interface)))
    .map((r) => r.name),
)
const canWrite = computed(() => auth.canWrite(name.value))
const canDelete = computed(() => auth.canDelete(name.value))

// RBAC editors on the generic form. Gated to super-admins — a non-admin with a read
// grant on role/user could open the form, but the matrix/preview endpoints would 403.
const savedRoleIsSuperAdmin = ref(false)
// The view owns the ONE leave guard for both the generic form and the matrix — see
// guardLeave() below, which folds in matrix.value?.dirty alongside the form's own dirty check.
const matrix = ref<InstanceType<typeof PermissionMatrix> | null>(null)
// Also shown in create mode, buffered locally by the matrix (no role id to GET/PUT
// against yet) — onSubmit's create path flushes the buffer once the role exists.
const showMatrix = computed(
  () => name.value === ROLE_COLLECTION && auth.user?.isSuperAdmin === true,
)
const showEffective = computed(
  () => !isCreate.value && name.value === USER_COLLECTION && auth.user?.isSuperAdmin === true,
)
// The effective-permissions preview follows the CURRENT (possibly unsaved) Roles
// TagSelect selection live, via the panel's own debounced watcher — not a post-save reload.
const selectedRoleIds = computed(() => (model.relations.roles as string[] | undefined) ?? [])

const model = reactive<FormModel>({ shared: {}, translations: {}, relations: {} })
const errors = ref<Record<string, string>>({})
const serverError = ref('')
const loading = ref(true)
const submitting = ref(false)
// ui/button has no `loading` prop (unlike the PrimeVue Button it replaces), so the in-flight
// Save state is surfaced through this label swap plus :disabled="submitting" on the button below.
const saveLabel = computed(() => (submitting.value ? t('itemForm.saving') : t('itemForm.save')))
const notFound = ref(false)
const conflict = ref(false)
const showHistory = ref(false)
// Holds the server copy fetched during 409 recovery so "Reload latest" can apply it verbatim.
let latestFromServer: FormModel | null = null
// Last committed snapshot of the user-editable model; the leave/unload guards compare against it.
// Seeded with the empty model so navigating away before load never falsely prompts.
const baseline = ref(snapshotModel(model))

function captureBaseline(): void {
  baseline.value = snapshotModel(model)
}

function setModel(next: FormModel): void {
  model.shared = next.shared
  model.translations = next.translations
  model.relations = next.relations
  // Carry the optimistic-concurrency token so update payloads echo it. Without this
  // the version chain breaks at the view layer and the optimistic lock silently degrades to
  // last-write-wins. On create, next.version is undefined and stays undefined.
  model.version = next.version
  // Every setModel is a fresh committed state: initial load (create blank / edit fetch) and
  // "Reload latest". Re-baseline so it is not considered dirty. (recoverFromConflict deliberately
  // does NOT call setModel — it only refreshes version and keeps the user's edits, staying dirty.)
  captureBaseline()
}

async function init(): Promise<void> {
  loading.value = true
  serverError.value = ''
  notFound.value = false
  errors.value = {}
  // Clear stale 409-recovery state so a re-entrant init (route param change) never carries item A's
  // cached server copy or conflict banner into item B.
  conflict.value = false
  latestFromServer = null
  await Promise.all([schema.load(), langStore.load()])
  if (!meta.value) { loading.value = false; return }
  if (isCreate.value) {
    setModel(blankItemForm(meta.value, langStore.languages))
  } else {
    try {
      const item = await itemsApi.get(name.value, id.value!, {
        deep: editableRelations.value,
        locale: langStore.defaultCode,
      })
      setModel(parseItemToForm(meta.value, item, langStore.languages))
      // The matrix's super-admin notice reflects the last-SAVED state, not the unsaved checkbox.
      if (name.value === ROLE_COLLECTION)
        savedRoleIsSuperAdmin.value = model.shared.isSuperAdmin === true
    } catch (e) {
      if (e instanceof ApiError && e.code === 'NOT_FOUND') notFound.value = true
      else serverError.value = e instanceof Error ? e.message : t('common.loadFailed')
    }
  }
  loading.value = false
}

async function onSubmit(): Promise<void> {
  if (!meta.value) return
  errors.value = validateItem(meta.value, model, langStore.defaultCode)
  if (Object.keys(errors.value).length > 0) return
  submitting.value = true
  serverError.value = ''
  try {
    const payload = buildItemPayload(meta.value, model, langStore.languages, isCreate.value ? 'create' : 'update')
    // Capture the created item so a role-create can PUT its buffered grants against the
    // freshly-minted id (created.id) below.
    const created = isCreate.value ? await itemsApi.create(name.value, payload) : undefined
    const updated = !isCreate.value ? await itemsApi.update(name.value, id.value!, payload) : undefined
    conflict.value = false
    // The update DID succeed even if the matrix flush below fails — refresh the
    // concurrency token from the response and re-baseline the FORM now, before the matrix flush.
    // Returning early (matrix failure) must never discard a successful update: doing so left the
    // saved edits reading dirty (bogus unsaved-changes prompt on leave) and a retry echoing the
    // stale version (bogus 409 VERSION_CONFLICT against the user's own prior save).
    if (updated && typeof (updated as Record<string, unknown>).version === 'number') {
      model.version = (updated as Record<string, unknown>).version as number
    }
    // Editing the Language collection changes which locale tabs every other form shows.
    if (name.value === LANGUAGE_COLLECTION) await langStore.reload()
    if (name.value === ROLE_COLLECTION) savedRoleIsSuperAdmin.value = model.shared.isSuperAdmin === true
    captureBaseline() // form saved successfully: clear its own dirty flag before the matrix flush
    // One form Save also flushes a dirty permission matrix, so the user only has to click
    // Save once. The matrix flush runs AFTER the form is already baselined above: if the matrix's
    // own PUT fails, its own error toast already fired, and we keep the user on the page (matrix
    // stays dirty, form does not) instead of navigating away and losing the unsaved grants.
    if (!isCreate.value && name.value === ROLE_COLLECTION && matrix.value?.dirty) {
      const ok = await matrix.value.save()
      if (!ok) return
    }
    // Role create — the matrix has no role id to PUT against until now, so it only buffers
    // toggles locally. Flush that buffer against the id the create just returned. On failure, the
    // role itself DID get created: warn and route to its edit page (where the matrix can retry)
    // instead of the normal list navigation, which would otherwise hide the lost grants.
    if (isCreate.value && name.value === ROLE_COLLECTION && created) {
      const entries = matrix.value?.currentEntries() ?? []
      if (entries.length > 0) {
        try {
          await rbacApi.putRolePermissions(String(created.id), entries)
        } catch {
          toast.add({ severity: 'warn', summary: t('rbac.grantsSaveFailedAfterCreate'), life: 6000 })
          // The role WAS created, only the grants PUT failed. Re-baseline the matrix
          // before navigating to its edit page (the form itself was already re-baselined above) —
          // the create-mode buffer is discarded on this remount anyway (the edit-mode matrix
          // instance re-GETs grants from the server), so nothing is lost, and leaving the matrix
          // baseline stale would make the unified leave guard block the very navigation this
          // failure path performs.
          matrix.value?.markFlushed()
          router.push({ name: 'collection-item', params: { name: name.value, id: String(created.id) } })
          return
        }
      }
      // Grants are now flushed (PUT succeeded above) or there was nothing to flush
      // (buffer held only all-false rows, e.g. toggled back off) — either way re-baseline the
      // matrix so `dirty` clears. Without this, create mode's baseline never leaves '{}' and the
      // unified leave guard fires an "Unsaved changes" prompt on the successful navigation below;
      // picking "stay" there would leave the user on the create form, risking a duplicate role on
      // a second Save.
      matrix.value?.markFlushed()
    }
    // Note: the form itself was already re-baselined above (right after the create/update calls),
    // before either matrix flush — no second captureBaseline() needed here.
    router.push({ name: 'collection-list', params: { name: name.value } })
  } catch (e) {
    if (e instanceof ApiError && e.status === 409 && e.code === 'VERSION_CONFLICT') {
      // Optimistic-lock clash: only this specific code means "someone else changed the
      // item since we loaded it". Recover by refreshing the concurrency token WITHOUT touching the
      // user's in-progress edits, then let them either save again (overwrite) or reload the server
      // copy. Other 409s (delete-restrict, duplicate email) keep the generic CONFLICT code and must
      // NOT arm this recovery banner — they fall through to the serverError banner below. No
      // fallback to 'CONFLICT' here is deliberate. VERSION_CONFLICT carries no details, so it never
      // overlaps the details-mapping branch below.
      await recoverFromConflict()
    } else if (e instanceof ApiError && e.details?.length) {
      // Server-side (ASP.NET model-binding) validation: map details back to
      // per-field errors; anything not matching a field falls to the banner.
      const knownFields = new Set((meta.value?.fields ?? []).map((f) => f.name))
      const { fieldErrors, leftover } = splitServerErrors(e.details, knownFields)
      // Replace (not mutate) so ItemForm's watch(props.errors) flips to the default locale tab.
      errors.value = { ...fieldErrors }
      const banner = leftover.join('; ')
      serverError.value = banner || (Object.keys(fieldErrors).length === 0 ? e.message : '')
    } else {
      serverError.value = e instanceof Error ? e.message : t('common.saveFailed')
    }
  } finally {
    submitting.value = false
  }
}

async function recoverFromConflict(): Promise<void> {
  if (!meta.value) return
  try {
    const latest = await itemsApi.get(name.value, id.value!, {
      deep: editableRelations.value,
      locale: langStore.defaultCode,
    })
    latestFromServer = parseItemToForm(meta.value, latest, langStore.languages)
    // Only refresh the token; keep the user's edits so a re-save overwrites the server copy.
    model.version = latestFromServer.version
    conflict.value = true
  } catch (e) {
    // The item may have been deleted in the interim: fall back to the existing NOT_FOUND / error UI.
    conflict.value = false
    if (e instanceof ApiError && e.code === 'NOT_FOUND') notFound.value = true
    else serverError.value = e instanceof Error ? e.message : t('common.reloadFailed')
  }
}

function reloadLatest(): void {
  if (!latestFromServer) return
  setModel(latestFromServer) // full overwrite: discard the user's edits for the server copy
  // setModel aliases (does not clone) the objects, so drop the cached copy: subsequent user edits
  // would otherwise silently mutate this "server copy" if it were reused.
  latestFromServer = null
  conflict.value = false
  errors.value = {}
  serverError.value = ''
  // The matrix's super-admin notice reflects the last-SAVED state, not the unsaved checkbox.
  if (name.value === ROLE_COLLECTION)
    savedRoleIsSuperAdmin.value = model.shared.isSuperAdmin === true
}

async function onReverted(): Promise<void> {
  if (!meta.value || id.value === undefined) return
  // The POST /revert response omits translations + relations, so re-fetch the full item (same shape
  // as the edit-load) rather than trusting the emitted payload. setModel re-baselines, so the form
  // is not considered dirty afterward. The drawer owns the success toast.
  try {
    const item = await itemsApi.get(name.value, id.value, {
      deep: editableRelations.value,
      locale: langStore.defaultCode,
    })
    setModel(parseItemToForm(meta.value, item, langStore.languages))
    errors.value = {}
    serverError.value = ''
  } catch (e) {
    serverError.value = e instanceof Error ? e.message : t('common.reloadFailed')
  }
}

async function onDelete(): Promise<void> {
  const { header, message } = deleteConfirm(t, deleteKindFor(meta.value))
  // severity 'danger' is the confirmStore vocabulary (not the toast one) and drives the
  // destructive variant on ConfirmHost's accept button.
  if (!(await confirm.require({ header, message, severity: 'danger' }))) return
  try {
    await itemsApi.remove(name.value, id.value!)
    // Editing the Language collection changes which locale tabs every other form shows.
    if (name.value === LANGUAGE_COLLECTION) await langStore.reload()
    captureBaseline() // item is gone: nothing to lose, so leaving must not prompt
    router.push({ name: 'collection-list', params: { name: name.value } })
  } catch (e) {
    serverError.value = e instanceof Error ? e.message : t('common.deleteFailed')
  }
}

function onCancel(): void {
  // Routes back — naturally intercepted by the leave guard below, so no extra prompt here.
  router.push({ name: 'collection-list', params: { name: name.value } })
}

// Warn before navigating away (SPA route change) with unsaved edits. Registered
// synchronously in setup so vue-router picks it up. Returns a Promise the router awaits:
// resolve(true) allows the navigation, resolve(false) cancels it and keeps the user here.
function guardLeave(): Promise<boolean> {
  // Unified guard — also dirty if the mounted permission matrix (Role edit) has unsaved
  // grants, so a single confirm covers both instead of two independently-registered guards firing
  // sequentially on the same navigation.
  if (!(isDirty(baseline.value, model) || (matrix.value?.dirty ?? false))) return Promise.resolve(true)
  const { header, message } = unsavedConfirm(t)
  // Cancel and dismiss (Escape — an AlertDialog is not backdrop-dismissible, and ConfirmHost
  // renders no close button) are the same outcome here by design: confirmStore guarantees every
  // request settles its Promise exactly once, so there is no third "closed without answering"
  // state that would otherwise leave this navigation awaiting forever.
  return confirm.require({ header, message })
}
onBeforeRouteLeave(() => guardLeave())
// A same-route-record, params-only navigation (RelatedList row click, create -> edit) does
// NOT trigger onBeforeRouteLeave — the router treats it as an update of the reused component. Run the
// same dirty guard here so unsaved edits are not silently discarded. Only guard an actual record
// switch (id or collection changed); a query-only change keeps the user on the same item, so allow it.
onBeforeRouteUpdate(async (to, from) => {
  if (to.params.id !== from.params.id || to.params.name !== from.params.name) return guardLeave()
  return true
})

// Warn before a full browser unload (tab close / reload / hard navigation) with unsaved
// edits. The browser shows its own native dialog — preventDefault is all that is needed; custom
// text is not honoured by modern browsers.
function onBeforeUnload(e: BeforeUnloadEvent): void {
  if (isDirty(baseline.value, model) || (matrix.value?.dirty ?? false)) {
    e.preventDefault()
    e.returnValue = '' // legacy Chrome/Firefox: a truthy returnValue triggers the prompt
  }
}
onMounted(() => window.addEventListener('beforeunload', onBeforeUnload))
onBeforeUnmount(() => window.removeEventListener('beforeunload', onBeforeUnload))

onMounted(init)
defineExpose({ init, onSubmit, onDelete, onCancel, reloadLatest, onReverted, showHistory, model, errors, serverError, notFound, loading, conflict, guardLeave })
</script>

<template>
  <section class="item-form-view grid gap-1">
    <p v-if="loading" class="text-muted-foreground">{{ t('itemForm.loading') }}</p>
    <p v-else-if="!meta" class="text-muted-foreground">{{ t('itemForm.collectionNotFound') }}</p>
    <p v-else-if="notFound" class="text-muted-foreground">{{ t('itemForm.itemNotFound') }}</p>
    <p v-else-if="isCreate && !canWrite" class="text-muted-foreground">{{ t('itemForm.noCreatePermission') }}</p>
    <template v-else>
      <PageHeader :title="isCreate ? t('itemForm.new', { label: meta.label }) : t('itemForm.edit', { label: meta.label })">
        <template #lead>
          <Button type="button" variant="ghost" size="icon" :aria-label="t('itemForm.back')" @click="onCancel">
            <ChevronLeft class="size-4" />
          </Button>
        </template>
        <template #actions>
          <Button v-if="!isCreate && meta.revisions" type="button" variant="ghost" @click="showHistory = true">
            <History class="size-4" />{{ t('revisions.open') }}
          </Button>
          <Button v-if="!isCreate && canDelete" type="button" variant="destructive" @click="onDelete">
            {{ t('itemForm.delete') }}
          </Button>
          <Button v-if="canWrite" type="button" :disabled="submitting" @click="onSubmit">{{ saveLabel }}</Button>
        </template>
      </PageHeader>

      <div
        v-if="conflict"
        class="conflict-banner mb-4 flex items-center gap-4 rounded-md border border-warning bg-warning/10 px-4 py-3 text-foreground"
        role="alert"
      >
        <span class="conflict-text flex-1">{{ t('itemForm.conflictText') }}</span>
        <Button type="button" variant="outline" size="sm" @click="reloadLatest">{{ t('itemForm.reloadLatest') }}</Button>
      </div>

      <ItemForm
        :meta="meta"
        :item-id="id"
        :model="model"
        :locales="langStore.languages"
        :errors="errors"
        :server-error="serverError"
        :disabled="!canWrite"
        @submit="onSubmit"
      />

      <PermissionMatrix
        v-if="showMatrix"
        ref="matrix"
        :role-id="idStr"
        :is-super-admin-role="savedRoleIsSuperAdmin"
        :create-mode="isCreate"
      />
      <EffectivePermissionsPanel v-if="showEffective" :user-id="idStr" :role-ids="selectedRoleIds" />

      <RevisionHistoryDrawer
        v-if="!isCreate && meta.revisions"
        v-model:visible="showHistory"
        :collection="name"
        :item-id="idStr"
        :can-revert="canWrite"
        @reverted="onReverted"
      />
    </template>
  </section>
</template>
