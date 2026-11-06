<script setup lang="ts">
import { ref, computed, onMounted, useId } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from '@/composables/useToast'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { rbacApi, type CapabilityEntry } from '../../api/rbacApi'
import { traceIdDetail } from '../../api/apiClient'

// The Role form's system capabilities: the catalog from GET /capabilities, one checkbox each, saved
// as a full-replace PUT of the ticked codes. Independent of the permission matrix's own save. The
// parent mounts it for an existing role that is neither `public` nor a super-admin role; for a role
// that does not exist yet, or for `public`, `unavailable` swaps the list for a muted explanation.
const props = defineProps<{ roleId: string; unavailable?: 'create' | 'public' }>()

const { t, te, fallbackLocale } = useI18n()
const toast = useToast()
const uid = useId()

const catalog = ref<CapabilityEntry[]>([])
const selected = ref<string[]>([])
const baseline = ref('[]')
const loading = ref(true)
const loadFailed = ref(false)
const saving = ref(false)

const snapshot = (codes: string[]): string => JSON.stringify([...codes].sort())
const dirty = computed(() => snapshot(selected.value) !== baseline.value)
const saveLabel = computed(() => (saving.value ? t('rbac.capabilitiesSaving') : t('rbac.capabilitiesSave')))
const unavailableKey = computed(() =>
  props.unavailable === 'create' ? 'rbac.capabilitiesCreateHint' : 'rbac.capabilitiesPublicNotice',
)

// A fork's capability may ship without a locale entry in every language: the fallback locales are
// consulted first, and only a key found nowhere lets the code stand in for the text.
function fallbackLocales(): string[] {
  const fb = fallbackLocale.value
  if (typeof fb === 'string') return [fb]
  return Array.isArray(fb) ? fb : []
}
function hasText(key: string): boolean {
  if (te(key)) return true
  return fallbackLocales().some((locale) => te(key, locale))
}
function textFor(key: string, fallback: string): string {
  return hasText(key) ? t(key) : fallback
}

function setHeld(code: string, held: boolean): void {
  const without = selected.value.filter((c) => c !== code)
  selected.value = held ? [...without, code] : without
}

async function load(): Promise<void> {
  loading.value = true
  loadFailed.value = false
  try {
    const [entries, held] = await Promise.all([
      rbacApi.listCapabilities(),
      rbacApi.getRoleCapabilities(props.roleId),
    ])
    catalog.value = entries
    selected.value = [...held]
    baseline.value = snapshot(held)
  } catch {
    loadFailed.value = true
  } finally {
    loading.value = false
  }
}

// Returns whether the save succeeded so the parent form's Save can flush this panel as part of one
// submit and stay on the page when the PUT fails (its own error toast still fires).
async function save(): Promise<boolean> {
  saving.value = true
  try {
    const saved = await rbacApi.putRoleCapabilities(props.roleId, [...selected.value].sort())
    selected.value = [...saved]
    baseline.value = snapshot(saved)
    toast.add({ severity: 'success', summary: t('rbac.capabilitiesSaved'), life: 3000 })
    return true
  } catch (e) {
    toast.add({ severity: 'error', summary: t('rbac.capabilitiesSaveFailed'), detail: traceIdDetail(e), life: 5000 })
    return false
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  if (props.unavailable) loading.value = false
  else void load()
})
defineExpose({ save, dirty, load, saveLabel })
</script>

<template>
  <section class="role-capabilities">
    <h2 class="panel-title">{{ t('rbac.capabilitiesTitle') }}</h2>

    <p v-if="unavailable" class="text-muted-foreground">{{ t(unavailableKey) }}</p>
    <p v-else-if="loadFailed" class="text-muted-foreground">{{ t('rbac.capabilitiesLoadFailed') }}</p>
    <template v-else-if="!loading">
      <p v-if="catalog.length === 0" class="text-muted-foreground">{{ t('rbac.capabilitiesEmpty') }}</p>
      <ul v-else class="capability-list">
        <li v-for="(c, i) in catalog" :key="c.code" class="capability-item">
          <Checkbox :id="`${uid}-cap-${i}`" :model-value="selected.includes(c.code)"
                    :aria-describedby="textFor(c.descriptionKey, '') ? `${uid}-desc-${i}` : undefined"
                    @update:model-value="(v) => setHeld(c.code, !!v)" />
          <div class="capability-text">
            <label :for="`${uid}-cap-${i}`" class="capability-label">{{ textFor(c.labelKey, c.code) }}</label>
            <p v-if="textFor(c.descriptionKey, '')" :id="`${uid}-desc-${i}`"
               class="capability-description text-muted-foreground">
              {{ textFor(c.descriptionKey, '') }}
            </p>
          </div>
        </li>
      </ul>
      <div v-if="catalog.length > 0" class="panel-actions">
        <Button type="button" :disabled="!dirty || saving" @click="save">{{ saveLabel }}</Button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.role-capabilities { display: grid; gap: 12px; margin-top: 24px; }
.panel-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.capability-list { display: grid; gap: 12px; list-style: none; margin: 0; padding: 0; }
.capability-item { display: flex; align-items: flex-start; gap: 12px; }
.capability-text { display: grid; gap: 2px; }
.capability-label { font-weight: 500; }
.capability-description { margin: 0; font-size: 0.9rem; }
.panel-actions { display: flex; justify-content: flex-end; }
</style>
