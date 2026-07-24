<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { onBeforeRouteLeave } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import { useSchemaStore } from '../../stores/schemaStore'
import { rbacApi, type RolePermissionEntry } from '../../api/rbacApi'
import { unsavedConfirm } from '../../lib/formDirty'

// 問題 3: the Role form's permission matrix — one row per collection, read/write/delete grants,
// saved as a full-replace PUT independent from the generic form's save.
const props = defineProps<{ roleId: string; isSuperAdminRole: boolean }>()

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const schema = useSchemaStore()

type Grant = { read: boolean; write: boolean; delete: boolean }
const grants = ref<Record<string, Grant>>({})
const baseline = ref('{}')
const loading = ref(true)
const loadFailed = ref(false)
const saving = ref(false)

// Every collection is a row — including hidden ones (grants on `file` etc. still matter for
// API consumers). AdminOnly rows keep Read grantable; writes are super-admin-only by design.
const rows = computed(() =>
  [...schema.collections].sort(
    (a, b) => (a.group ?? '').localeCompare(b.group ?? '') || a.label.localeCompare(b.label),
  ),
)

const dirty = computed(() => JSON.stringify(grants.value) !== baseline.value)

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

async function save(): Promise<void> {
  saving.value = true
  try {
    const entries: RolePermissionEntry[] = Object.entries(grants.value)
      .filter(([, g]) => g.read || g.write || g.delete)
      .map(([collection, g]) => ({
        collection, canRead: g.read, canWrite: g.write, canDelete: g.delete,
      }))
    grants.value = fold(await rbacApi.putRolePermissions(props.roleId, entries))
    baseline.value = JSON.stringify(grants.value)
    toast.add({ severity: 'success', summary: t('rbac.saved'), life: 3000 })
  } catch {
    toast.add({ severity: 'error', summary: t('rbac.saveFailed'), life: 5000 })
  } finally {
    saving.value = false
  }
}

onMounted(() => {
  if (!props.isSuperAdminRole) void load()
  else loading.value = false
})

// Route-leave guard for unsaved matrix edits. Uses the parent-provided ConfirmDialog
// (ItemFormView mounts one) — mounting a second instance here would duplicate dialogs (FE-R7).
onBeforeRouteLeave(() => {
  if (!dirty.value) return true
  const { header, message } = unsavedConfirm(t)
  return new Promise<boolean>((resolve) => {
    confirm.require({
      header,
      message,
      accept: () => resolve(true),
      reject: () => resolve(false),
      onHide: () => resolve(false), // dismiss = stay (matches ItemFormView.guardLeave)
    })
  })
})

defineExpose({ toggle, save, dirty, load })
</script>

<template>
  <section class="permission-matrix">
    <h2 class="matrix-title">{{ t('rbac.matrixTitle') }}</h2>

    <p v-if="isSuperAdminRole" class="notice">{{ t('rbac.superAdminAll') }}</p>
    <p v-else-if="loadFailed" class="notice">{{ t('rbac.loadFailed') }}</p>
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
                <Checkbox :model-value="grantFor(c.name).read" binary
                          @update:model-value="(v: boolean) => toggle(c.name, 'read', !!v)" />
              </td>
              <td :title="c.adminOnly ? t('rbac.adminOnlyWriteHint') : undefined">
                <Checkbox :model-value="grantFor(c.name).write" binary :disabled="!!c.adminOnly"
                          @update:model-value="(v: boolean) => toggle(c.name, 'write', !!v)" />
              </td>
              <td :title="c.adminOnly ? t('rbac.adminOnlyWriteHint') : undefined">
                <Checkbox :model-value="grantFor(c.name).delete" binary :disabled="!!c.adminOnly"
                          @update:model-value="(v: boolean) => toggle(c.name, 'delete', !!v)" />
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="matrix-actions">
        <Button :label="t('rbac.save')" :disabled="!dirty || saving" :loading="saving" @click="save" />
      </div>
    </template>
  </section>
</template>

<style scoped>
.permission-matrix { display: grid; gap: 12px; margin-top: 24px; }
.matrix-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.notice { color: var(--muted); margin: 0; }
.matrix-scroll { overflow-x: auto; }
.matrix-table { border-collapse: collapse; min-width: 480px; }
.matrix-table th, .matrix-table td { padding: 8px 16px; text-align: center; border-bottom: 1px solid var(--border); }
.matrix-table .col-name { text-align: left; }
.matrix-actions { display: flex; justify-content: flex-end; }
</style>
