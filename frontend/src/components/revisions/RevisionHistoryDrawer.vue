<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import Drawer from 'primevue/drawer'
import Button from 'primevue/button'
import ConfirmDialog from 'primevue/confirmdialog'
import { useConfirm } from 'primevue/useconfirm'
import { useToast } from 'primevue/usetoast'
import { useI18n } from 'vue-i18n'
import RevisionSnapshotView from './RevisionSnapshotView.vue'
import { revisionOperationKey } from '../../lib/revisionOperation'
import { itemsApi, type RevisionInfo, type RevisionDetail } from '../../api/itemsApi'

const props = defineProps<{
  visible: boolean
  collection: string
  itemId: string
  canRevert: boolean
}>()
const emit = defineEmits<{
  (e: 'update:visible', value: boolean): void
  (e: 'reverted', item: Record<string, unknown>): void
}>()

const confirm = useConfirm()
const toast = useToast()
const { t } = useI18n()

const revisions = ref<RevisionInfo[]>([])
const selected = ref<RevisionInfo | null>(null)
const detail = ref<RevisionDetail | null>(null)
const listLoading = ref(false)
const listError = ref('')
const detailLoading = ref(false)
const detailError = ref('')
const reverting = ref(false)

const isEmpty = computed(() => !listLoading.value && !listError.value && revisions.value.length === 0)

function opLabel(op: string): string {
  return t(revisionOperationKey(op))
}
function whenLabel(iso: string): string {
  return new Date(iso).toLocaleString()
}

async function load(): Promise<void> {
  listLoading.value = true
  listError.value = ''
  selected.value = null
  detail.value = null
  try {
    revisions.value = await itemsApi.listRevisions(props.collection, props.itemId)
  } catch {
    listError.value = t('revisions.loadError')
  } finally {
    listLoading.value = false
  }
}

async function select(rev: RevisionInfo): Promise<void> {
  selected.value = rev
  detail.value = null
  detailLoading.value = true
  detailError.value = ''
  try {
    detail.value = await itemsApi.getRevision(props.collection, props.itemId, rev.revisionNumber)
  } catch {
    detailError.value = t('revisions.detailError')
  } finally {
    detailLoading.value = false
  }
}

function onRevert(n: number): void {
  confirm.require({
    group: 'revisions',
    header: t('revisions.revertConfirmHeader'),
    message: t('revisions.revertConfirmMessage', { n }),
    accept: async () => {
      reverting.value = true
      try {
        const item = await itemsApi.revert(props.collection, props.itemId, n)
        emit('reverted', item)
        await load()
        toast.add({ severity: 'success', summary: t('revisions.reverted', { n }), life: 2500 })
      } catch {
        toast.add({ severity: 'error', summary: t('revisions.revertFailed'), life: 3500 })
      } finally {
        reverting.value = false
      }
    },
  })
}

watch(
  () => props.visible,
  (open) => { if (open) void load() },
  { immediate: true },
)

defineExpose({ load, select, onRevert, revisions, selected, detail, listLoading, listError, detailLoading, detailError, reverting })
</script>

<template>
  <Drawer
    :visible="visible"
    position="right"
    :header="t('revisions.title')"
    class="rev-drawer"
    :style="{ width: '46rem', maxWidth: '100vw' }"
    @update:visible="(v: boolean) => emit('update:visible', v)"
  >
    <ConfirmDialog group="revisions" />
    <div class="rev-layout">
      <aside class="rev-list">
        <p v-if="listLoading" class="rev-notice">{{ t('revisions.loading') }}</p>
        <div v-else-if="listError" class="rev-notice rev-error" role="alert">
          <span>{{ listError }}</span>
          <Button :label="t('revisions.retry')" size="small" text @click="load" />
        </div>
        <p v-else-if="isEmpty" class="rev-notice">{{ t('revisions.empty') }}</p>
        <ul v-else class="rev-items">
          <li v-for="rev in revisions" :key="rev.revisionNumber">
            <button
              type="button"
              class="rev-item"
              :class="{ 'rev-item--active': selected?.revisionNumber === rev.revisionNumber }"
              @click="select(rev)"
            >
              <span class="rev-item__num">#{{ rev.revisionNumber }}</span>
              <span class="rev-item__op">{{ opLabel(rev.operation) }}</span>
              <span class="rev-item__when">{{ whenLabel(rev.createdAt) }}</span>
            </button>
          </li>
        </ul>
      </aside>
      <RevisionSnapshotView
        class="rev-pane"
        :detail="detail"
        :loading="detailLoading"
        :error="detailError"
        :can-revert="canRevert && !reverting"
        @revert="onRevert"
      />
    </div>
  </Drawer>
</template>

<style scoped>
.rev-layout { display: grid; grid-template-columns: 16rem 1fr; gap: 20px; height: 100%; min-height: 0; }
.rev-list { border-right: 1px solid var(--border); padding-right: 12px; overflow: auto; }
.rev-notice { color: var(--muted); margin: 0; display: flex; align-items: center; gap: 8px; }
.rev-error { color: var(--danger); }
.rev-items { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
.rev-item {
  width: 100%; text-align: left; display: grid; gap: 2px; cursor: pointer;
  padding: 8px 10px; border: 1px solid transparent; border-radius: var(--radius, 8px);
  background: transparent; color: var(--fg);
}
.rev-item:hover { background: color-mix(in srgb, var(--fg) 6%, transparent); }
.rev-item--active { border-color: var(--border); background: color-mix(in srgb, var(--primary, #38bdf8) 10%, transparent); }
.rev-item__num { font-weight: 700; font-variant-numeric: tabular-nums; }
.rev-item__op { font-size: .85rem; }
.rev-item__when { font-size: .75rem; color: var(--muted); }
.rev-pane { min-width: 0; overflow: auto; }
@media (max-width: 640px) { .rev-layout { grid-template-columns: 1fr; } }
</style>
