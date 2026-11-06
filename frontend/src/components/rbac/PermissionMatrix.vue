<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from '@/composables/useToast'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi, type RolePermissionEntry } from '../../api/rbacApi'
import { traceIdDetail } from '../../api/apiClient'
import { GOVERNED_JUNCTIONS } from '../../lib/frameworkCollections'

// The Role form's permission matrix — one row per collection, read/write/delete grants,
// saved as a full-replace PUT independent from the generic form's save.
// createMode buffers grants locally (no GET, no own Save button) — ItemFormView's create
// submit reads currentEntries() and PUTs them itself once the role exists.
// `readonly` shows the grants without letting them change (a caller who can read roles but not write them).
const props = withDefaults(defineProps<{
  roleId: string; isSuperAdminRole: boolean; createMode?: boolean; readonly?: boolean
}>(), {
  createMode: false, readonly: false,
})

const { t } = useI18n()
const toast = useToast()
const schema = useSchemaStore()

type Grant = { read: boolean; write: boolean; delete: boolean }
const grants = ref<Record<string, Grant>>({})
const baseline = ref('{}')
const loading = ref(true)
const loadFailed = ref(false)
const saving = ref(false)

// Every collection is a row — including hidden ones (grants on `file` etc. still matter for
// API consumers) — except the hidden junctions, which follow the collection that owns them. A
// collection a fork marks AdminOnly keeps Read grantable; its writes stay super-admin-only.
const rows = computed(() =>
  schema.collections.filter((c) => !GOVERNED_JUNCTIONS.includes(c.name)).sort(
    (a, b) => (a.group ?? '').localeCompare(b.group ?? '') || a.label.localeCompare(b.label),
  ),
)

const dirty = computed(() => JSON.stringify(grants.value) !== baseline.value)
const saveLabel = computed(() => (saving.value ? t('rbac.saving') : t('rbac.save')))

function grantFor(name: string): Grant {
  return grants.value[name] ?? { read: false, write: false, delete: false }
}

function toggle(name: string, key: keyof Grant, value: boolean): void {
  // Immutable update: fresh record per change.
  grants.value = { ...grants.value, [name]: { ...grantFor(name), [key]: value } }
}

function fold(entries: RolePermissionEntry[]): Record<string, Grant> {
  const next: Record<string, Grant> = {}
  for (const e of entries) next[e.collection] = { read: e.canRead, write: e.canWrite, delete: e.canDelete }
  return next
}

async function load(): Promise<void> {
  loading.value = true
  loadFailed.value = false
  try {
    grants.value = fold(await rbacApi.getRolePermissions(props.roleId))
    baseline.value = JSON.stringify(grants.value)
  } catch {
    loadFailed.value = true
  } finally {
    loading.value = false
  }
}

// Shared fold from the live `grants` buffer down to the non-all-false wire entries — used
// by both save() (edit mode) and currentEntries() (create mode) so they cannot drift apart.
function foldEntries(): RolePermissionEntry[] {
  return Object.entries(grants.value)
    .filter(([, g]) => g.read || g.write || g.delete)
    .map(([collection, g]) => ({
      collection, canRead: g.read, canWrite: g.write, canDelete: g.delete,
    }))
}

// Returns whether the save succeeded so the parent form's Save can flush this matrix as
// part of one submit and stay on the page when the PUT fails (its own error toast still fires).
async function save(): Promise<boolean> {
  saving.value = true
  try {
    grants.value = fold(await rbacApi.putRolePermissions(props.roleId, foldEntries()))
    baseline.value = JSON.stringify(grants.value)
    toast.add({ severity: 'success', summary: t('rbac.saved'), life: 3000 })
    return true
  } catch (e) {
    toast.add({ severity: 'error', summary: t('rbac.saveFailed'), detail: traceIdDetail(e), life: 5000 })
    return false
  } finally {
    saving.value = false
  }
}

// Create-mode buffer read — ItemFormView calls this after the role is created to PUT the
// grants the user staged before the role existed.
function currentEntries(): RolePermissionEntry[] {
  return foldEntries()
}

// After ItemFormView flushes the create-mode buffer (PUT succeeded, or there
// was nothing to PUT), the live `grants` must be re-baselined here too — otherwise `dirty` stays
// true forever (create mode's baseline never moves off '{}') and the unified leave guard in
// ItemFormView keeps firing "Unsaved changes" even immediately after a successful save.
function markFlushed(): void {
  baseline.value = JSON.stringify(grants.value)
}

onMounted(() => {
  // Create mode has no role yet to GET permissions for — stay with the empty buffer.
  if (props.createMode) { loading.value = false; return }
  if (!props.isSuperAdminRole) void load()
  else loading.value = false
})

// No route-leave guard here — ItemFormView owns ONE unified guard that also checks this
// matrix's `dirty` (via the exposed computed below). Two guards registered independently would
// fire sequentially on the same navigation, producing two identical "Unsaved changes" dialogs.
defineExpose({ toggle, save, dirty, load, currentEntries, markFlushed, saveLabel })
</script>

<template>
  <section class="permission-matrix">
    <h2 class="matrix-title">{{ t('rbac.matrixTitle') }}</h2>

    <p v-if="isSuperAdminRole" class="notice text-muted-foreground">{{ t('rbac.superAdminAll') }}</p>
    <p v-else-if="loadFailed" class="notice text-muted-foreground">{{ t('rbac.loadFailed') }}</p>
    <template v-else-if="!loading">
      <div class="matrix-scroll">
        <table class="matrix-table">
          <thead>
            <tr>
              <th class="col-name">{{ t('rbac.colCollection') }}</th>
              <th>{{ t('rbac.colRead') }}</th>
              <th>{{ t('rbac.colWrite') }}</th>
              <th>{{ t('rbac.colDelete') }}</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="c in rows" :key="c.name">
              <td class="col-name">{{ c.label }}</td>
              <td>
                <Checkbox :model-value="grantFor(c.name).read" :disabled="readonly"
                          :aria-label="`${c.label} — ${t('rbac.colRead')}`"
                          @update:model-value="(v) => toggle(c.name, 'read', !!v)" />
              </td>
              <td :title="c.adminOnly ? t('rbac.adminOnlyWriteHint') : undefined">
                <Checkbox :model-value="grantFor(c.name).write" :disabled="!!c.adminOnly || readonly"
                          :aria-label="`${c.label} — ${t('rbac.colWrite')}`"
                          @update:model-value="(v) => toggle(c.name, 'write', !!v)" />
              </td>
              <td :title="c.adminOnly ? t('rbac.adminOnlyWriteHint') : undefined">
                <Checkbox :model-value="grantFor(c.name).delete" :disabled="!!c.adminOnly || readonly"
                          :aria-label="`${c.label} — ${t('rbac.colDelete')}`"
                          @update:model-value="(v) => toggle(c.name, 'delete', !!v)" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div v-if="!createMode && !readonly" class="matrix-actions">
        <Button type="button" :disabled="!dirty || saving" @click="save">{{ saveLabel }}</Button>
      </div>
    </template>
  </section>
</template>

<style scoped>
.permission-matrix { display: grid; gap: 12px; margin-top: 24px; }
.matrix-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.matrix-scroll { overflow-x: auto; }
.matrix-table { border-collapse: collapse; min-width: 480px; }
.matrix-table th, .matrix-table td { padding: 8px 16px; text-align: center; border-bottom: 1px solid var(--border); }
.matrix-table .col-name { text-align: left; }
.matrix-actions { display: flex; justify-content: flex-end; }
</style>
