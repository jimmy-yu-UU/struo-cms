<script setup lang="ts">
import { reactive, ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { useRoute, useRouter, onBeforeRouteLeave } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import ConfirmDialog from 'primevue/confirmdialog'
import Button from 'primevue/button'
import ItemForm from '../components/ItemForm.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { itemsApi } from '../api/itemsApi'
import { ApiError } from '../api/apiClient'
import { blankItemForm, parseItemToForm } from '../lib/parseItemToForm'
import { buildItemPayload } from '../lib/buildItemPayload'
import { validateItem } from '../lib/validateItem'
import { splitServerErrors } from '../lib/applyServerErrors'
import { relationInputKind } from '../lib/relationInputKind'
import { deleteKindFor, deleteConfirm } from '../lib/deleteAction'
import { snapshotModel, isDirty, unsavedConfirm } from '../lib/formDirty'
import type { FormModel } from '../types/itemForm'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()
const langStore = useLanguageStore()
const confirm = useConfirm()

const name = computed(() => route.params.name as string)
const id = computed(() => (route.params.id as string | undefined) ?? undefined)
const isCreate = computed(() => id.value === undefined)
const meta = computed(() => schema.get(name.value))
const editableRelations = computed(() =>
  (meta.value?.relations ?? [])
    .filter((r) => ['dropdown', 'tagSelect', 'treeSelect'].includes(relationInputKind(r.interface)))
    .map((r) => r.name),
)
const canWrite = computed(() => auth.canWrite(name.value))
const canDelete = computed(() => auth.canDelete(name.value))

const model = reactive<FormModel>({ shared: {}, translations: {}, relations: {} })
const errors = ref<Record<string, string>>({})
const serverError = ref('')
const loading = ref(true)
const submitting = ref(false)
const notFound = ref(false)
const conflict = ref(false)
// Holds the server copy fetched during 409 recovery so "Reload latest" can apply it verbatim.
let latestFromServer: FormModel | null = null
// Last committed snapshot of the user-editable model; the leave/unload guards compare against it
// (FE-5). Seeded with the empty model so navigating away before load never falsely prompts.
const baseline = ref(snapshotModel(model))

function captureBaseline(): void {
  baseline.value = snapshotModel(model)
}

function setModel(next: FormModel): void {
  model.shared = next.shared
  model.translations = next.translations
  model.relations = next.relations
  // Carry the optimistic-concurrency token so update payloads echo it (D2 / FE-4). Without this
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
    } catch (e) {
      if (e instanceof ApiError && e.code === 'NOT_FOUND') notFound.value = true
      else serverError.value = e instanceof Error ? e.message : 'Failed to load item.'
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
    if (isCreate.value) await itemsApi.create(name.value, payload)
    else await itemsApi.update(name.value, id.value!, payload)
    conflict.value = false
    captureBaseline() // saved successfully: clear dirty BEFORE navigating so the leave guard stays quiet
    router.push({ name: 'collection-list', params: { name: name.value } })
  } catch (e) {
    if (e instanceof ApiError && e.status === 409 && e.code === 'CONFLICT') {
      // Optimistic-lock clash (D2): someone else changed the item since we loaded it. Recover by
      // refreshing the concurrency token WITHOUT touching the user's in-progress edits, then let
      // them either save again (overwrite) or reload the server copy. CONFLICT carries no details,
      // so it never overlaps the FE-3 details-mapping branch below.
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
      serverError.value = e instanceof Error ? e.message : 'Save failed.'
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
    else serverError.value = e instanceof Error ? e.message : 'Failed to reload item.'
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
}

function onDelete(): void {
  const { header, message } = deleteConfirm(deleteKindFor(meta.value))
  confirm.require({
    header,
    message,
    accept: async () => {
      try {
        await itemsApi.remove(name.value, id.value!)
        captureBaseline() // item is gone: nothing to lose, so leaving must not prompt
        router.push({ name: 'collection-list', params: { name: name.value } })
      } catch (e) {
        serverError.value = e instanceof Error ? e.message : 'Delete failed.'
      }
    },
  })
}

function onCancel(): void {
  // Routes back — naturally intercepted by the leave guard below, so no extra prompt here.
  router.push({ name: 'collection-list', params: { name: name.value } })
}

// FE-5: warn before navigating away (SPA route change) with unsaved edits. Registered
// synchronously in setup so vue-router picks it up. Returns a Promise the router awaits:
// resolve(true) allows the navigation, resolve(false) cancels it and keeps the user here.
function guardLeave(): Promise<boolean> {
  if (!isDirty(baseline.value, model)) return Promise.resolve(true)
  const { header, message } = unsavedConfirm()
  return new Promise<boolean>((resolve) => {
    confirm.require({
      header,
      message,
      accept: () => resolve(true),
      reject: () => resolve(false),
    })
  })
}
onBeforeRouteLeave(() => guardLeave())

// FE-5: warn before a full browser unload (tab close / reload / hard navigation) with unsaved
// edits. The browser shows its own native dialog — preventDefault is all that is needed; custom
// text is not honoured by modern browsers.
function onBeforeUnload(e: BeforeUnloadEvent): void {
  if (isDirty(baseline.value, model)) {
    e.preventDefault()
    e.returnValue = '' // legacy Chrome/Firefox: a truthy returnValue triggers the prompt
  }
}
onMounted(() => window.addEventListener('beforeunload', onBeforeUnload))
onBeforeUnmount(() => window.removeEventListener('beforeunload', onBeforeUnload))

onMounted(init)
defineExpose({ init, onSubmit, onDelete, onCancel, reloadLatest, model, errors, serverError, notFound, loading, conflict })
</script>

<template>
  <section class="item-form-view">
    <ConfirmDialog />
    <p v-if="loading" class="notice">Loading…</p>
    <p v-else-if="!meta" class="notice">Collection not found.</p>
    <p v-else-if="notFound" class="notice">Item not found.</p>
    <p v-else-if="isCreate && !canWrite" class="notice">You don't have permission to create items here.</p>
    <template v-else>
      <header class="form-header">
        <h2>{{ isCreate ? `New ${meta.label}` : `Edit ${meta.label}` }}</h2>
        <Button v-if="!isCreate && canDelete" label="Delete" severity="danger" @click="onDelete" />
      </header>
      <div v-if="conflict" class="conflict-banner" role="alert">
        <span class="conflict-text">This item was changed by someone else. Review your edits and save again to overwrite, or reload the latest version.</span>
        <Button label="Reload latest" severity="secondary" size="small" @click="reloadLatest" />
      </div>
      <ItemForm
        :meta="meta"
        :item-id="id"
        :model="model"
        :locales="langStore.languages"
        :errors="errors"
        :server-error="serverError"
        :disabled="!canWrite"
        :submitting="submitting"
        @submit="onSubmit"
        @cancel="onCancel"
      />
    </template>
  </section>
</template>

<style scoped>
.conflict-banner {
  display: flex;
  align-items: center;
  gap: 1rem;
  padding: 0.75rem 1rem;
  margin-bottom: 1rem;
  border: 1px solid var(--p-amber-400, #f0ad4e);
  background: var(--p-amber-50, #fff8ec);
  border-radius: 6px;
}
.conflict-text {
  flex: 1;
}
</style>
