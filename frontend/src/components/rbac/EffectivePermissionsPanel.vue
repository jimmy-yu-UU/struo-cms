<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi, type EffectivePermissions } from '../../api/rbacApi'

// 問題 4: read-only preview of a user's merged grants, fed by the same resolver the backend
// authorizes with. Parent (ItemFormView) calls reload() after a successful save.
const props = defineProps<{ userId: string }>()

const { t } = useI18n()
const schema = useSchemaStore()

const eff = ref<EffectivePermissions | null>(null)
const loadFailed = ref(false)

const rows = computed(() => {
  if (!eff.value) return []
  return Object.entries(eff.value.permissions)
    .map(([name, g]) => ({ name, label: schema.get(name)?.label ?? name, ...g }))
    .sort((a, b) => a.label.localeCompare(b.label))
})

async function reload(): Promise<void> {
  loadFailed.value = false
  try {
    eff.value = await rbacApi.getEffectivePermissions(props.userId)
  } catch {
    loadFailed.value = true
    eff.value = null
  }
}

onMounted(reload)
defineExpose({ reload })
</script>

<template>
  <section class="effective-permissions">
    <h2 class="panel-title">{{ t('rbac.effectiveTitle') }}</h2>

    <p v-if="loadFailed" class="notice">{{ t('rbac.effectiveLoadFailed') }}</p>
    <p v-else-if="eff?.isSuperAdmin" class="notice">{{ t('rbac.effectiveSuperAdmin') }}</p>
    <p v-else-if="eff && rows.length === 0" class="notice">{{ t('rbac.effectiveEmpty') }}</p>
    <div v-else-if="eff" class="panel-scroll">
      <table class="panel-table">
        <thead>
          <tr>
            <th class="col-name">{{ t('rbac.colCollection') }}</th>
            <th>{{ t('rbac.colRead') }}</th>
            <th>{{ t('rbac.colWrite') }}</th>
            <th>{{ t('rbac.colDelete') }}</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="r in rows" :key="r.name">
            <td class="col-name">{{ r.label }}</td>
            <td><i v-if="r.read" class="pi pi-check" aria-hidden="true" /><span class="sr-only">{{ r.read }}</span></td>
            <td><i v-if="r.write" class="pi pi-check" aria-hidden="true" /><span class="sr-only">{{ r.write }}</span></td>
            <td><i v-if="r.delete" class="pi pi-check" aria-hidden="true" /><span class="sr-only">{{ r.delete }}</span></td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>

<style scoped>
.effective-permissions { display: grid; gap: 12px; margin-top: 24px; }
.panel-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.notice { color: var(--muted); margin: 0; }
.panel-scroll { overflow-x: auto; }
.panel-table { border-collapse: collapse; min-width: 480px; }
.panel-table th, .panel-table td { padding: 8px 16px; text-align: center; border-bottom: 1px solid var(--border); }
.panel-table .col-name { text-align: left; }
.sr-only { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); }
</style>
