<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from '@/composables/useToast'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { rbacApi, type CapabilityEntry } from '../../api/rbacApi'

// The Role form's system capabilities: the catalog from GET /capabilities, one checkbox each, saved
// as a full-replace PUT of the ticked codes. Independent of the permission matrix's own save. The
// parent mounts it for an existing role that is neither `public` nor a super-admin role.
const props = defineProps<{ roleId: string }>()

const { t, te } = useI18n()
const toast = useToast()

const catalog = ref<CapabilityEntry[]>([])
const selected = ref<string[]>([])
const baseline = ref('[]')
const loading = ref(true)
const loadFailed = ref(false)
const saving = ref(false)

const snapshot = (codes: string[]): string => JSON.stringify([...codes].sort())
const dirty = computed(() => snapshot(selected.value) !== baseline.value)
const saveLabel = computed(() => (saving.value ? t('rbac.capabilitiesSaving') : t('rbac.capabilitiesSave')))

// A fork's capability may ship without a locale entry; the code then stands in for the missing text.
function textFor(key: string, fallback: string): string {
  return te(key) ? t(key) : fallback
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
  } catch {
    toast.add({ severity: 'error', summary: t('rbac.capabilitiesSaveFailed'), life: 5000 })
    return false
  } finally {
    saving.value = false
  }
}

onMounted(load)
defineExpose({ save, dirty, load, saveLabel })
</script>

<template>
  <section class="role-capabilities">
    <h2 class="panel-title">{{ t('rbac.capabilitiesTitle') }}</h2>

    <p v-if="loadFailed" class="text-muted-foreground">{{ t('rbac.capabilitiesLoadFailed') }}</p>
    <template v-else-if="!loading">
      <p v-if="catalog.length === 0" class="text-muted-foreground">{{ t('rbac.capabilitiesEmpty') }}</p>
      <ul v-else class="capability-list">
        <li v-for="c in catalog" :key="c.code" class="capability-item">
          <Checkbox :id="`capability-${c.code}`" :model-value="selected.includes(c.code)"
                    @update:model-value="(v) => setHeld(c.code, !!v)" />
          <div class="capability-text">
            <label :for="`capability-${c.code}`" class="capability-label">{{ textFor(c.labelKey, c.code) }}</label>
            <p class="capability-description text-muted-foreground">{{ textFor(c.descriptionKey, '') }}</p>
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
