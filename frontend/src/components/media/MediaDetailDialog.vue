<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import TreeSelect from 'primevue/treeselect'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useI18n } from 'vue-i18n'
import { Copy, Trash2 } from '@lucide/vue'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle, DialogFooter } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { filesApi } from '../../api/filesApi'
import { ApiError } from '../../api/apiClient'
import { useSchemaStore } from '../../stores/schemaStore'
import { useLanguageStore } from '../../stores/languageStore'
import { parseItemToForm } from '../../lib/parseItemToForm'
import { buildItemPayload } from '../../lib/buildItemPayload'
import { deleteConfirm } from '../../lib/deleteAction'
import { formatFileSize } from '../../lib/formatFileSize'
import { formatDateTime } from '../../lib/formatDateTime'
import { buildRelationTree, type TreeNode } from '../../lib/buildRelationTree'
import { toFolderRows, type FolderRow } from '../../lib/folderTree'
import type { FormModel } from '../../types/itemForm'

const props = defineProps<{ file: FileRow | null; canWrite: boolean; canDelete: boolean }>()
const emit = defineEmits<{ (e: 'close'): void; (e: 'saved'): void; (e: 'deleted'): void }>()

const UNFILED = '__unfiled'

const confirm = useConfirm()
const toast = useToast()
const { t } = useI18n()
const schema = useSchemaStore()
const langStore = useLanguageStore()

const visible = computed(() => props.file !== null)
const raw = ref<Record<string, unknown> | null>(null)
const model = ref<FormModel>({ shared: {}, translations: {}, relations: {} })
const activeLocale = ref('')
const loading = ref(false)
const saving = ref(false)
const conflict = ref(false)
const error = ref('')
const folders = ref<FolderRow[]>([])
const folderId = ref<string | null>(null)

const fileMeta = computed(() => schema.get('file'))
const locales = computed(() => langStore.languages)
const localeOptions = computed(() =>
  locales.value.map((l) => ({ label: l.name, value: l.code })),
)

const isImage = computed(() => (props.file?.contentType ?? '').startsWith('image/'))
const previewSrc = computed(() => (props.file ? filesApi.contentUrl(props.file.id) : ''))
const dimensions = computed(() => {
  const w = raw.value?.width as number | undefined
  const h = raw.value?.height as number | undefined
  return w && h ? `${w}×${h}` : '—'
})
const sizeText = computed(() =>
  typeof raw.value?.size === 'number' ? formatFileSize(raw.value.size as number) : '—',
)
const uploadedText = computed(() =>
  typeof raw.value?.createdAt === 'string' ? formatDateTime(raw.value.createdAt as string) : '—',
)
const statusText = computed(() => (raw.value?.status as string | undefined) ?? '—')
const fileUrl = computed(() => (props.file ? filesApi.contentUrl(props.file.id) : ''))

const folderNodes = computed<TreeNode[]>(() => [
  { key: UNFILED, label: t('media.folderUncategorized'), data: UNFILED, children: [] },
  ...buildRelationTree(folders.value.map((f) => ({ id: f.id, label: f.name, parentId: f.parentId })), 'parentId'),
])
// TreeSelect single-selection binds { [key]: true } (same mapping as RelationPicker's tree mode).
const folderValue = computed(() => ({ [folderId.value ?? UNFILED]: true }))
function onFolderChange(selection: Record<string, boolean>): void {
  const key = Object.keys(selection)[0]
  folderId.value = !key || key === UNFILED ? null : key
}

const activeValues = computed<Record<string, unknown>>({
  get: () => model.value.translations[activeLocale.value] ?? {},
  set: (v) => { model.value = { ...model.value, translations: { ...model.value.translations, [activeLocale.value]: v } } },
})

function setField(name: 'title' | 'alt', value: string): void {
  activeValues.value = { ...activeValues.value, [name]: value }
}

function onLocaleToggle(value: unknown): void {
  // reka's single-type ToggleGroup emits undefined when the pressed item is clicked again, and a
  // locale switcher has no "no locale" state to fall into.
  if (typeof value === 'string' && value.length > 0) activeLocale.value = value
}

// ui/button has no `loading` prop (unlike the PrimeVue Button it replaces), so the in-flight save
// state is surfaced through this label swap plus :disabled on the button.
const saveLabel = computed(() => (saving.value ? t('media.saving') : t('media.save')))

async function load(): Promise<void> {
  if (!props.file) return
  loading.value = true
  conflict.value = false
  error.value = ''
  try {
    await Promise.all([schema.load(), langStore.load()])
    activeLocale.value = langStore.defaultCode
    // The items API only projects M2O relation FKs under `deep` expansion, nested as
    // `folder: { id, ... }` under the relation's nav-property name -- it never returns a flat
    // `folderId` column. Without `deep`, item.folderId is always undefined, so every save from
    // this dialog would silently send folderId: null and unfile the file.
    const item = await itemsApi.get('file', props.file.id, { deep: ['folder'] })
    raw.value = item
    if (fileMeta.value) model.value = parseItemToForm(fileMeta.value, item, locales.value)
    folderId.value = (item.folder as { id?: string } | undefined)?.id ?? null
    try {
      const res = await itemsApi.list('mediafolder', { page: 0, rows: 500, sort: 'name', deep: ['parent'] })
      folders.value = toFolderRows(res.data)
    } catch {
      // Folder loading is a progressive enhancement: if it fails, degrade to a flat "Uncategorized
      // only" picker rather than blocking the whole detail dialog.
      folders.value = []
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : t('media.loadFailed')
  } finally {
    loading.value = false
  }
}

async function onSave(): Promise<void> {
  if (!props.file || !fileMeta.value) return
  saving.value = true
  conflict.value = false
  error.value = ''
  try {
    const payload = { ...buildItemPayload(fileMeta.value, model.value, locales.value, 'update'), folderId: folderId.value }
    await itemsApi.update('file', props.file.id, payload)
    emit('saved')
    emit('close')
  } catch (e) {
    if (e instanceof ApiError && e.status === 409 && e.code === 'VERSION_CONFLICT') {
      // Optimistic-lock clash: someone else changed the item since we loaded it. Recover by
      // refreshing only the concurrency token, preserving the user's in-progress edits, so the
      // next Save overwrites with the current version instead of 409-ing forever.
      conflict.value = true
      await recoverFromConflict()
    } else {
      error.value = e instanceof Error ? e.message : t('media.saveFailed')
    }
  } finally {
    saving.value = false
  }
}

async function recoverFromConflict(): Promise<void> {
  if (!props.file) return
  try {
    const fresh = await itemsApi.get('file', props.file.id)
    const version = typeof fresh.version === 'number' ? (fresh.version as number) : undefined
    model.value = { ...model.value, version }
  } catch (e) {
    // The file may have been deleted in the interim: surface via the existing error banner
    // rather than throwing uncaught. conflict stays true so the notice remains visible.
    error.value = e instanceof Error ? e.message : t('media.saveFailed')
  }
}

function onDelete(): void {
  if (!props.file) return
  const id = props.file.id
  confirm.require({
    ...deleteConfirm(t, 'soft'),
    accept: async () => {
      try {
        await filesApi.remove(id)
        emit('deleted')
        emit('close')
      } catch (e) {
        error.value = e instanceof Error ? e.message : t('media.deleteFailed')
      }
    },
  })
}

async function onCopyUrl(): Promise<void> {
  try {
    await navigator.clipboard.writeText(fileUrl.value)
    toast.add({ severity: 'success', summary: t('media.urlCopied'), life: 2000 })
  } catch {
    // Clipboard unavailable (e.g. insecure context, permission denied) — surface it
    // rather than silently doing nothing, so the user knows the copy did not happen.
    toast.add({ severity: 'error', summary: t('media.copyFailed'), life: 3500 })
  }
}

watch(() => props.file?.id, (id) => { if (id) void load() }, { immediate: true })

defineExpose({ model, conflict, onSave, onDelete, onCopyUrl, activeLocale, setField, folderId, onFolderChange, error, saveLabel, onLocaleToggle })
</script>

<template>
  <Dialog :open="visible" @update:open="(v: boolean) => { if (!v) emit('close') }">
    <!--
      DialogScrollContent, not DialogContent: the preview image plus the field column can exceed the
      viewport, and reka's DialogRoot locks body scroll while open, leaving a fixed-position centered
      box with no scroll container at all. Its own width class is a BARE max-w-lg (DialogContent's
      is sm:max-w-lg), so this override supplies no modifier either -- tailwind-merge keys on
      (modifier set, class group), and a bare max-w-* only loses to another bare max-w-*.
      max-[960px]:max-w-[95vw] reproduces the old :breakpoints="{ '960px': '95vw' }".
    -->
    <DialogScrollContent class="max-w-[min(78vw,1100px)] max-[960px]:max-w-[95vw]">
      <ConfirmDialog />
      <DialogHeader>
        <DialogTitle>{{ $t('media.detailTitle') }}</DialogTitle>
      </DialogHeader>

      <div v-if="file" class="md-grid">
        <div class="md-preview">
          <img v-if="isImage" :src="previewSrc" :alt="file.fileName" />
          <FileThumbnail v-else :file="file" />
        </div>
        <div class="md-fields">
          <ToggleGroup
            v-if="localeOptions.length > 1"
            type="single"
            variant="outline"
            :model-value="activeLocale"
            @update:model-value="onLocaleToggle"
          >
            <ToggleGroupItem v-for="o in localeOptions" :key="o.value" :value="o.value">
              {{ o.label }}
            </ToggleGroupItem>
          </ToggleGroup>

          <label class="md-field">
            <span>{{ $t('media.fieldTitle') }}</span>
            <Input
              :model-value="(activeValues.title as string) ?? ''"
              :disabled="!canWrite || loading"
              @update:model-value="setField('title', String($event ?? ''))"
            />
          </label>
          <label class="md-field">
            <span>{{ $t('media.fieldAlt') }}</span>
            <Input
              :model-value="(activeValues.alt as string) ?? ''"
              :disabled="!canWrite || loading"
              @update:model-value="setField('alt', String($event ?? ''))"
            />
          </label>
          <label class="md-field">
            <span>{{ $t('media.folderField') }}</span>
            <TreeSelect
              :model-value="folderValue"
              :options="folderNodes"
              selection-mode="single"
              :disabled="!canWrite || loading"
              @update:model-value="onFolderChange"
            />
          </label>

          <div class="md-kv"><span>{{ $t('media.colDimensions') }}</span><b>{{ dimensions }}</b></div>
          <div class="md-kv"><span>{{ $t('media.colSize') }}</span><b>{{ sizeText }}</b></div>
          <div class="md-kv"><span>{{ $t('media.colUploaded') }}</span><b>{{ uploadedText }}</b></div>
          <div class="md-kv"><span>{{ $t('media.status') }}</span><b>{{ statusText }}</b></div>

          <label class="md-field">
            <span>{{ $t('media.fileUrl') }}</span>
            <div class="md-url">
              <Input :model-value="fileUrl" readonly />
              <Button
                type="button" variant="ghost" size="icon"
                :aria-label="$t('media.copyUrl')" @click="onCopyUrl"
              >
                <Copy aria-hidden="true" />
              </Button>
            </div>
          </label>

          <p v-if="conflict" class="md-conflict" role="alert">{{ $t('media.saveConflict') }}</p>
          <p v-if="error" class="md-error" role="alert">{{ error }}</p>
        </div>
      </div>

      <DialogFooter class="md-foot">
        <Button
          v-if="canDelete"
          type="button" variant="ghost"
          class="mr-auto text-destructive hover:text-destructive"
          @click="onDelete"
        >
          <Trash2 aria-hidden="true" />
          {{ $t('media.delete') }}
        </Button>
        <Button v-if="canWrite" type="button" :disabled="saving" @click="onSave">{{ saveLabel }}</Button>
      </DialogFooter>
    </DialogScrollContent>
  </Dialog>
</template>

<style scoped>
.md-grid { display: grid; grid-template-columns: 240px 1fr; gap: 20px; }
.md-preview {
  border: 1px solid var(--border); border-radius: var(--legacy-radius-lg, 12px);
  background: var(--surface-2); padding: 10px; display: flex; align-items: center; justify-content: center;
  min-height: 200px; overflow: hidden;
}
.md-preview img { max-width: 100%; max-height: 260px; object-fit: contain; border-radius: var(--legacy-radius, 8px); }
.md-fields { display: grid; gap: 12px; align-content: start; }
.md-field { display: grid; gap: 4px; }
.md-field > span { font-size: .8rem; color: var(--legacy-muted); font-weight: 500; }
.md-kv { display: flex; justify-content: space-between; gap: 12px; font-size: .85rem; }
.md-kv span { color: var(--legacy-muted); }
.md-kv b { color: var(--fg); font-weight: 600; }
.md-url { display: flex; gap: 6px; align-items: center; }
.md-url :deep(input) { flex: 1; }
.md-conflict { margin: 0; color: var(--warn); font-size: .85rem; }
.md-error { margin: 0; color: var(--danger); font-size: .85rem; }
@media (max-width: 640px) { .md-grid { grid-template-columns: 1fr; } }
</style>
