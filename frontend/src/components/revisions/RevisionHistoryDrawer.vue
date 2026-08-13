<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { Sheet, SheetContent, SheetHeader, SheetTitle } from '@/components/ui/sheet'
import { Button } from '@/components/ui/button'
import { useConfirm } from '@/composables/useConfirm'
import { useToast } from '@/composables/useToast'
import { useI18n } from 'vue-i18n'
import RevisionSnapshotView from './RevisionSnapshotView.vue'
import { revisionOperationKey } from '../../lib/revisionOperation'
import { formatRevisionTime } from '../../lib/formatRevisionTime'
import { createLatestWins } from '../../lib/latestWins'
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

function isActiveRev(rev: RevisionInfo): boolean {
  return selected.value?.revisionNumber === rev.revisionNumber
}

const detailLoad = createLatestWins()

function opLabel(op: string): string {
  return t(revisionOperationKey(op))
}
function whenLabel(iso: string): string {
  return formatRevisionTime(iso)
}

async function load(): Promise<void> {
  listLoading.value = true
  listError.value = ''
  selected.value = null
  detail.value = null
  detailLoad.next() // invalidate any in-flight select() detail load so a stale response can't repaint after a reload
  detailError.value = ''
  detailLoading.value = false
  try {
    revisions.value = await itemsApi.listRevisions(props.collection, props.itemId)
  } catch {
    listError.value = t('revisions.loadError')
  } finally {
    listLoading.value = false
  }
}

async function select(rev: RevisionInfo): Promise<void> {
  const token = detailLoad.next()
  selected.value = rev
  detail.value = null
  detailLoading.value = true
  detailError.value = ''
  try {
    const res = await itemsApi.getRevision(props.collection, props.itemId, rev.revisionNumber)
    if (!detailLoad.isCurrent(token)) return
    detail.value = res
  } catch {
    if (!detailLoad.isCurrent(token)) return
    detailError.value = t('revisions.detailError')
  } finally {
    if (detailLoad.isCurrent(token)) detailLoading.value = false
  }
}

async function onRevert(n: number): Promise<void> {
  if (reverting.value) return
  const accepted = await confirm.require({
    header: t('revisions.revertConfirmHeader'),
    message: t('revisions.revertConfirmMessage', { n }),
  })
  if (!accepted) return
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
}

watch(
  () => props.visible,
  (open) => { if (open) void load() },
  { immediate: true },
)

defineExpose({ load, select, onRevert, revisions, selected, detail, listLoading, listError, detailLoading, detailError, reverting })
</script>

<template>
  <Sheet :open="visible" @update:open="(v: boolean) => emit('update:visible', v)">
    <!--
      SheetContent side="right" carries a bare w-3/4 AND an sm:max-w-sm. tailwind-merge keys
      conflicts on (modifier set, class group), so the bare w-[46rem] displaces w-3/4 while
      sm:max-w-sm needs an sm:-prefixed max-width of its own to lose. 46rem is wider than the
      640px sm breakpoint, so the sm: override is 100vw rather than 46rem — otherwise the sheet
      overflows the viewport between 640px and 736px.
    -->
    <SheetContent side="right" class="w-[46rem] max-w-[100vw] sm:max-w-[100vw]">
      <SheetHeader class="px-6">
        <SheetTitle>{{ t('revisions.title') }}</SheetTitle>
      </SheetHeader>
      <div class="rev-layout">
        <aside class="rev-list">
          <p v-if="listLoading" class="rev-notice text-muted-foreground">{{ t('revisions.loading') }}</p>
          <div v-else-if="listError" class="rev-notice rev-error" role="alert">
            <span>{{ listError }}</span>
            <!-- variant="ghost" declares no base text colour, so without this it would inherit
                 .rev-error's danger red at rest and jump to hover:text-accent-foreground on
                 hover; text-foreground makes the rest state deliberate and matches every other
                 ghost button's hover, unmodified. -->
            <Button
              type="button"
              variant="ghost"
              size="sm"
              class="text-foreground"
              data-test="rev-retry"
              @click="load"
            >
              {{ t('revisions.retry') }}
            </Button>
          </div>
          <p v-else-if="isEmpty" class="rev-notice text-muted-foreground">{{ t('revisions.empty') }}</p>
          <ul v-else class="rev-items">
            <li v-for="rev in revisions" :key="rev.revisionNumber">
              <button
                type="button"
                class="rev-item rounded-md"
                :class="{ 'rev-item--active': isActiveRev(rev), 'bg-primary/10': isActiveRev(rev) }"
                @click="select(rev)"
              >
                <span class="rev-item__num">#{{ rev.revisionNumber }}</span>
                <span class="rev-item__op">{{ opLabel(rev.operation) }}</span>
                <span class="rev-item__when text-muted-foreground">{{ whenLabel(rev.createdAt) }}</span>
              </button>
            </li>
          </ul>
        </aside>
        <RevisionSnapshotView
          class="rev-pane"
          :detail="detail"
          :loading="detailLoading"
          :error="detailError"
          :can-revert="canRevert"
          :reverting="reverting"
          @revert="onRevert"
        />
      </div>
    </SheetContent>
  </Sheet>
</template>

<style scoped>
.rev-layout { display: grid; grid-template-columns: 16rem 1fr; gap: 20px; flex: 1; min-height: 0; padding: 0 1.5rem 1.5rem; }
.rev-list { border-right: 1px solid var(--border); padding-right: 12px; overflow: auto; }
.rev-notice { margin: 0; display: flex; align-items: center; gap: 8px; }
.rev-error { color: var(--danger); }
.rev-items { list-style: none; margin: 0; padding: 0; display: grid; gap: 4px; }
.rev-item {
  width: 100%; text-align: left; display: grid; gap: 2px; cursor: pointer;
  padding: 8px 10px; border: 1px solid transparent; color: var(--fg);
}
.rev-item:hover { background: color-mix(in srgb, var(--fg) 6%, transparent); }
.rev-item--active { border-color: var(--border); }
.rev-item__num { font-weight: 700; font-variant-numeric: tabular-nums; }
.rev-item__op { font-size: .85rem; }
.rev-item__when { font-size: .75rem; }
.rev-pane { min-width: 0; overflow: auto; }
@media (max-width: 640px) { .rev-layout { grid-template-columns: 1fr; } }
</style>
