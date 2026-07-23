<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { useConfirm } from 'primevue/useconfirm'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import ConfirmDialog from 'primevue/confirmdialog'
import InputText from 'primevue/inputtext'
import PageHeader from '../components/common/PageHeader.vue'
import FilePicker from '../components/fields/FilePicker.vue'
import MediaUploadDropzone from '../components/media/MediaUploadDropzone.vue'
import { useAppConfigStore } from '../stores/appConfigStore'
import { useAuthStore } from '../stores/authStore'
import { unsavedConfirm } from '../lib/formDirty'
import type { FileMeta } from '../api/filesApi'

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const cfg = useAppConfigStore()
const auth = useAuthStore()

const isAdmin = computed(() => auth.user?.isSuperAdmin === true)

const brandName = ref('')
const logoFileId = ref<string | null>(null)
const saving = ref(false)

// Baseline snapshot of the editable fields, captured on load and re-captured on every successful
// save; the leave guard below compares against it (spec §4.3/§5, mirrors ItemFormView's FE-5
// dirty guard). A plain JSON.stringify is enough here — the settings model is a flat pair of
// primitives, not the nested FormModel that lib/formDirty.ts's snapshotModel/isDirty target.
function snapshot(): string {
  return JSON.stringify({ brandName: brandName.value, logoFileId: logoFileId.value })
}
const baseline = ref(snapshot())
function captureBaseline(): void {
  baseline.value = snapshot()
}
const dirty = computed(() => snapshot() !== baseline.value)

onMounted(() => {
  brandName.value = cfg.brandName
  // Recover the logo file id from the config URL (/api/files/{id}/content), if any.
  const m = cfg.brandLogoUrl?.match(/\/api\/files\/([^/]+)\/content/)
  logoFileId.value = m ? m[1] : null
  captureBaseline()
})

function onUploaded(meta: FileMeta): void {
  logoFileId.value = meta.id
}

async function save(): Promise<void> {
  const name = brandName.value.trim()
  if (name.length === 0) { toast.add({ severity: 'warn', summary: t('settings.nameRequired'), life: 3000 }); return }
  if (name.length > 100) { toast.add({ severity: 'warn', summary: t('settings.nameTooLong'), life: 3000 }); return }
  saving.value = true
  try {
    await cfg.saveBranding({ brandName: name, logoFileId: logoFileId.value })
    captureBaseline() // saved successfully: clear dirty
    toast.add({ severity: 'success', summary: t('settings.saved'), life: 3000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: t('settings.saveFailed'),
      detail: e instanceof Error ? e.message : undefined, life: 5000 })
  } finally {
    saving.value = false
  }
}

// Mirrors ItemFormView's FE-5 guardLeave: warn before navigating away (SPA route change) with
// unsaved edits, only while an admin (the only role that can actually edit these fields). Reuses
// the same unsavedConfirm() copy as the item form rather than duplicating an i18n key.
function guardLeave(): Promise<boolean> {
  if (!isAdmin.value || !dirty.value) return Promise.resolve(true)
  const { header, message } = unsavedConfirm(t)
  return new Promise<boolean>((resolve) => {
    confirm.require({
      header,
      message,
      accept: () => resolve(true),
      reject: () => resolve(false),
      // Esc / backdrop / X dismiss fires neither accept nor reject; onHide always fires on
      // dismissal, so treat it as "cancel navigation, stay here" (see ItemFormView fold-in c).
      onHide: () => resolve(false),
    })
  })
}
onBeforeRouteLeave(() => guardLeave())

// Mirrors ItemFormView's FE-5 onBeforeUnload guard: warn before a full browser unload (tab close /
// reload / hard navigation) with unsaved edits — guardLeave above only catches SPA route changes.
// The browser shows its own native dialog; preventDefault is all that is needed.
function onBeforeUnload(e: BeforeUnloadEvent): void {
  if (isAdmin.value && dirty.value) {
    e.preventDefault()
    e.returnValue = '' // legacy Chrome/Firefox: a truthy returnValue triggers the prompt
  }
}
onMounted(() => window.addEventListener('beforeunload', onBeforeUnload))
onBeforeUnmount(() => window.removeEventListener('beforeunload', onBeforeUnload))
</script>

<template>
  <ConfirmDialog />
  <div v-if="!isAdmin" class="settings-denied" role="alert">{{ t('settings.notPermitted') }}</div>
  <template v-else>
    <PageHeader :title="t('settings.title')" />
    <section class="settings-section">
      <h2>{{ t('settings.branding') }}</h2>

      <label class="settings-field">
        <span>{{ t('settings.brandName') }}</span>
        <InputText v-model="brandName" maxlength="100" />
      </label>

      <div class="settings-field">
        <span>{{ t('settings.logo') }}</span>
        <FilePicker v-model="logoFileId" image />
        <MediaUploadDropzone @uploaded="onUploaded" />
      </div>

      <div class="settings-actions">
        <Button data-test="save" :label="t('settings.save')" :loading="saving" @click="save" />
      </div>
    </section>
  </template>
</template>

<style scoped>
.settings-section { max-width: 640px; display: flex; flex-direction: column; gap: 20px; }
.settings-field { display: flex; flex-direction: column; gap: 8px; }
.settings-actions { margin-top: 8px; }
.settings-denied { padding: 24px; color: var(--muted); }
</style>
