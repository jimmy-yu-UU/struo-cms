<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { useRouter } from 'vue-router'
import Dialog from 'primevue/dialog'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import SelectButton from 'primevue/selectbutton'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useI18n } from 'vue-i18n'
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
import type { FormModel } from '../../types/itemForm'

const props = defineProps<{ file: FileRow | null; canWrite: boolean; canDelete: boolean }>()
const emit = defineEmits<{ (e: 'close'): void; (e: 'saved'): void; (e: 'deleted'): void }>()

const router = useRouter()
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
  typeof raw.value?.createdAt === 'string' ? new Date(raw.value.createdAt as string).toLocaleString() : '—',
)
const statusText = computed(() => (raw.value?.status as string | undefined) ?? '—')
const fileUrl = computed(() => (props.file ? filesApi.contentUrl(props.file.id) : ''))

const activeValues = computed<Record<string, unknown>>({
  get: () => model.value.translations[activeLocale.value] ?? {},
  set: (v) => { model.value = { ...model.value, translations: { ...model.value.translations, [activeLocale.value]: v } } },
})

function setField(name: 'title' | 'alt', value: string): void {
  activeValues.value = { ...activeValues.value, [name]: value }
}

async function load(): Promise<void> {
  if (!props.file) return
  loading.value = true
  conflict.value = false
  error.value = ''
  try {
    await Promise.all([schema.load(), langStore.load()])
    activeLocale.value = langStore.defaultCode
    const item = await itemsApi.get('file', props.file.id)
    raw.value = item
    if (fileMeta.value) model.value = parseItemToForm(fileMeta.value, item, locales.value)
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
    const payload = buildItemPayload(fileMeta.value, model.value, locales.value, 'update')
    await itemsApi.update('file', props.file.id, payload)
    emit('saved')
    emit('close')
  } catch (e) {
    if (e instanceof ApiError && e.status === 409 && e.code === 'VERSION_CONFLICT') {
      // Optimistic-lock clash (D2): someone else changed the item since we loaded it. Recover by
      // refreshing only the concurrency token, preserving the user's in-progress edits, so the
      // next Save overwrites with the current version instead of 409-ing forever (FE-18).
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
    ...deleteConfirm(t, 'hard'),
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
    // Clipboard unavailable (e.g. insecure context, permission denied) — surface it (SEC-12)
    // rather than silently doing nothing, so the user knows the copy did not happen.
    toast.add({ severity: 'error', summary: t('media.copyFailed'), life: 3500 })
  }
}

function onOpenEditor(): void {
  if (!props.file) return
  router.push({ name: 'collection-item', params: { name: 'file', id: props.file.id } })
}

watch(() => props.file?.id, (id) => { if (id) void load() }, { immediate: true })

defineExpose({ model, conflict, onSave, onDelete, onCopyUrl, activeLocale, setField })
</script>

<template>
  <Dialog
    :visible="visible"
    modal
    :header="$t('media.detailTitle')"
    :style="{ width: '52rem' }"
    :dismissable-mask="true"
    @update:visible="(v: boolean) => { if (!v) emit('close') }"
  >
    <ConfirmDialog />
    <div v-if="file" class="md-grid">
      <div class="md-preview">
        <img v-if="isImage" :src="previewSrc" :alt="file.fileName" />
        <FileThumbnail v-else :file="file" />
      </div>
      <div class="md-fields">
        <SelectButton
          v-if="localeOptions.length > 1"
          v-model="activeLocale"
          :options="localeOptions"
          option-label="label"
          option-value="value"
          :allow-empty="false"
        />
        <label class="md-field">
          <span>{{ $t('media.fieldTitle') }}</span>
          <InputText :model-value="(activeValues.title as string) ?? ''" @update:model-value="setField('title', $event ?? '')" :disabled="!canWrite || loading" />
        </label>
        <label class="md-field">
          <span>{{ $t('media.fieldAlt') }}</span>
          <InputText :model-value="(activeValues.alt as string) ?? ''" @update:model-value="setField('alt', $event ?? '')" :disabled="!canWrite || loading" />
        </label>

        <div class="md-kv"><span>{{ $t('media.colDimensions') }}</span><b>{{ dimensions }}</b></div>
        <div class="md-kv"><span>{{ $t('media.colSize') }}</span><b>{{ sizeText }}</b></div>
        <div class="md-kv"><span>{{ $t('media.colUploaded') }}</span><b>{{ uploadedText }}</b></div>
        <div class="md-kv"><span>{{ $t('media.status') }}</span><b>{{ statusText }}</b></div>

        <label class="md-field">
          <span>{{ $t('media.fileUrl') }}</span>
          <div class="md-url">
            <InputText :model-value="fileUrl" readonly />
            <Button icon="pi pi-copy" text :aria-label="$t('media.copyUrl')" @click="onCopyUrl" />
          </div>
        </label>

        <p v-if="conflict" class="md-conflict" role="alert">{{ $t('media.saveConflict') }}</p>
        <p v-if="error" class="md-error" role="alert">{{ error }}</p>
      </div>
    </div>

    <template #footer>
      <div class="md-foot">
        <Button
          v-if="canDelete"
          :label="$t('media.delete')"
          icon="pi pi-trash"
          text
          severity="danger"
          @click="onDelete"
        />
        <div class="md-foot__right">
          <Button :label="$t('media.openInEditor')" icon="pi pi-external-link" text severity="secondary" @click="onOpenEditor" />
          <Button v-if="canWrite" :label="$t('media.save')" :loading="saving" @click="onSave" />
        </div>
      </div>
    </template>
  </Dialog>
</template>

<style scoped>
.md-grid { display: grid; grid-template-columns: 240px 1fr; gap: 20px; }
.md-preview {
  border: 1px solid var(--border); border-radius: var(--radius-lg, 12px);
  background: var(--bg); padding: 10px; display: flex; align-items: center; justify-content: center;
  min-height: 200px; overflow: hidden;
}
.md-preview img { max-width: 100%; max-height: 260px; object-fit: contain; border-radius: var(--radius, 8px); }
.md-fields { display: grid; gap: 12px; align-content: start; }
.md-field { display: grid; gap: 4px; }
.md-field > span { font-size: .8rem; color: var(--muted); font-weight: 500; }
.md-kv { display: flex; justify-content: space-between; gap: 12px; font-size: .85rem; }
.md-kv span { color: var(--muted); }
.md-kv b { color: var(--fg); font-weight: 600; }
.md-url { display: flex; gap: 6px; align-items: center; }
.md-url :deep(input) { flex: 1; }
.md-conflict { margin: 0; color: var(--warn); font-size: .85rem; }
.md-error { margin: 0; color: var(--danger); font-size: .85rem; }
.md-foot { display: flex; align-items: center; justify-content: space-between; width: 100%; gap: 10px; }
.md-foot__right { display: flex; gap: 10px; align-items: center; margin-left: auto; }
@media (max-width: 640px) { .md-grid { grid-template-columns: 1fr; } }
</style>
