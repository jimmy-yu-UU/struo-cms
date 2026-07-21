<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Toast from 'primevue/toast'
import PageHeader from '../components/common/PageHeader.vue'
import FilePicker from '../components/fields/FilePicker.vue'
import MediaUploadDropzone from '../components/media/MediaUploadDropzone.vue'
import { useAppConfigStore } from '../stores/appConfigStore'
import { useAuthStore } from '../stores/authStore'
import type { FileMeta } from '../api/filesApi'

const { t } = useI18n()
const toast = useToast()
const cfg = useAppConfigStore()
const auth = useAuthStore()

const isAdmin = computed(() => auth.user?.isSuperAdmin === true)

const brandName = ref('')
const logoFileId = ref<string | null>(null)
const saving = ref(false)

onMounted(() => {
  brandName.value = cfg.brandName
  // Recover the logo file id from the config URL (/api/files/{id}/content), if any.
  const m = cfg.brandLogoUrl?.match(/\/api\/files\/([^/]+)\/content/)
  logoFileId.value = m ? m[1] : null
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
    toast.add({ severity: 'success', summary: t('settings.saved'), life: 3000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: t('settings.saveFailed'),
      detail: e instanceof Error ? e.message : undefined, life: 5000 })
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Toast />
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
.settings-denied { padding: 24px; color: var(--text); }
</style>
