<!-- frontend/src/views/CollectionListView.vue -->
<!-- No route-params watcher: AppShell's keyed <router-view> remounts this view on a collection
     switch (see AppShell.vue's :key="route.path" comment). -->
<script setup lang="ts">
import { ref, computed, onMounted, onBeforeUnmount, watch, h, useId } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { Plus, Pencil, Eye, Trash2, Undo2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import DataTable, { type DataTableColumn, type DataTableState, toSortParam } from '@/components/data/DataTable.vue'
import DataTablePagination from '@/components/data/DataTablePagination.vue'
import FilterBuilder, { type FilterField } from '@/components/data/FilterBuilder.vue'
import CreateUserDialog from '@/components/account/CreateUserDialog.vue'
import PageHeader from '../components/common/PageHeader.vue'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import { useConfirm } from '@/composables/useConfirm'
import { itemsApi } from '../api/itemsApi'
import { loginLocksApi, type LoginLockState } from '../api/loginLocksApi'
import { traceIdDetail } from '../api/apiClient'
import { writeFailureMessage } from '@/lib/apiErrorMessage'
import { useToast } from '@/composables/useToast'
import { selectListColumns, type ColumnDef as ListColumn } from '../lib/selectListColumns'
import { formatCell } from '../lib/formatCell'
import { deleteKindFor, deleteConfirm, purgeConfirm } from '../lib/deleteAction'
import { createLatestWins } from '../lib/latestWins'
import { primitiveText } from '../lib/primitiveText'
import { formatClock } from '../lib/formatClock'
import { pickTranslated, type TranslationMap } from '../lib/pickTranslated'
import { LANGUAGE_COLLECTION, PUBLIC_ROLE_NAME, ROLE_COLLECTION, USER_COLLECTION } from '../lib/frameworkCollections'
import type { CreatedUser } from '@/api/usersApi'
import type { FilterSpec } from '@/lib/buildListQuery'
import type { FieldMeta } from '../types/schema'

type Row = Record<string, unknown>

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const schema = useSchemaStore()
const langStore = useLanguageStore()
const confirm = useConfirm()
const toast = useToast()
const { t } = useI18n()

const name = computed(() => route.params.name as string)
const meta = computed(() => schema.get(name.value))
const canRead = computed(() => auth.canRead(name.value))
const canWrite = computed(() => auth.canWrite(name.value))
const canDelete = computed(() => auth.canDelete(name.value))
// Users are created through a dialog backed by POST /api/users (write on the user collection), not the
// generic item form, so `user` is creatable whatever `creatable` says.
const isUserCollection = computed(() => name.value === USER_COLLECTION)
// Nothing is creatable until the collection's meta has loaded: `creatable` is unknown before then.
const canCreate = computed(() => !!meta.value && canWrite.value && (
  isUserCollection.value || meta.value.creatable !== false
))
const createUserOpen = ref(false)
const mode = ref<'active' | 'trash'>('active')
const showTrashSwitch = computed(() => !!meta.value?.softDelete && canDelete.value)
const columns = computed(() => (meta.value ? selectListColumns(meta.value) : []))

const rows = ref<Row[]>([])
const total = ref(0)
// False until a list response has supplied `total`: the count caption and the pager range stay blank
// rather than reading 0, and later refreshes keep the last total.
const totalKnown = ref(false)
// True from the first render: the first list request starts only after the schema has loaded, and
// the table must not show its empty message in the meantime. loadItems clears it on every bail.
const loading = ref(true)
const error = ref('')
const tableState = ref<DataTableState>({ sort: [], page: 0, pageSize: 25 })
const filters = ref<FilterSpec>({})

function fieldOf(colField: string): FieldMeta | undefined {
  return meta.value?.fields.find((f) => f.name === colField)
}

function cellValue(row: Row, field: FieldMeta): unknown {
  if (field.translatable) {
    return pickTranslated(row.translations as TranslationMap, langStore.defaultCode, field.name)
  }
  return row[field.name]
}

function isSelectField(colField: string): boolean {
  return String(fieldOf(colField)?.interface ?? '').toLowerCase() === 'select'
}
function tagSeverity(v: unknown): 'success' | 'secondary' {
  const s = primitiveText(v).toLowerCase()
  if (s === 'published') return 'success'
  return 'secondary'
}
function formatDeletedAt(v: unknown): string {
  const raw = primitiveText(v)
  if (raw === '') return ''
  const d = new Date(raw)
  return Number.isNaN(d.getTime()) ? raw : d.toLocaleString()
}

// Fields FilterBuilder can offer: the display columns first, then every searchable field that did
// not make the ≤6-column display cut. selectListColumns filters by DISPLAY eligibility (a RichText
// body or a 7th-ranked slug never appears), while Searchable is what the backend's `search=`
// honours and what the manual tells collection authors governs free-text search — so without this
// second group those fields are unreachable from the list. Hidden fields are excluded because
// QueryValidator refuses to filter them at all. An enum (select) field surfaces its options so
// FilterBuilder renders a Select instead of a free-text Input for it.
const filterFields = computed<FilterField[]>(() => {
  const listed = columns.value.map((c: ListColumn) => {
    const field = fieldOf(c.field)
    const choices = (field?.options as { label: string; value: string }[] | undefined)
    return { name: c.field, label: c.header, options: isSelectField(c.field) ? choices : undefined }
  })
  const listedNames = new Set(listed.map((f) => f.name))
  const searchableOnly = (meta.value?.fields ?? [])
    .filter((f) => f.searchable && !f.hidden && !f.isSystem && !listedNames.has(f.name))
    .map((f) => ({
      name: f.name,
      label: f.label,
      options: isSelectField(f.name) ? (f.options as { label: string; value: string }[] | undefined) : undefined,
    }))
  return [...listed, ...searchableOnly]
})

// ---- login lock column (user collection) ----
// One batch query per loaded page. Each row shows one button: Not locked, Unlock (m:ss) for a lock the caller
// can clear, or Locked (m:ss) when it cannot. A failing query leaves the list as it is and marks the column
// unavailable. While any row is locked a single page-level timer ticks once a second; the time left is the
// queried retryAfterSeconds minus the time since the query returned, and when a row reaches zero the page is
// queried once more, whether one row or several expired in that tick.
const showLockColumn = computed(() => isUserCollection.value && mode.value === 'active' && canRead.value)
const lockStates = ref<Record<string, LoginLockState>>({})
const lockFailed = ref(false)
const lockLoaded = ref(false)
const lockQueriedAt = ref(0)
const lockNow = ref(Date.now())
const unlockingEmail = ref<string | null>(null)
const lockLoad = createLatestWins()
const anyLocked = computed(() => Object.values(lockStates.value).some((state) => state.locked))
let lockTimer: ReturnType<typeof setInterval> | undefined
let expiryQueryPending = false

function emailOf(row: Row): string {
  return primitiveText(row.email)
}

function secondsLeft(state: LoginLockState): number {
  return Math.max(0, state.retryAfterSeconds - Math.floor((lockNow.value - lockQueriedAt.value) / 1000))
}

function tickLocks(): void {
  lockNow.value = Date.now()
  if (expiryQueryPending) return
  if (!Object.values(lockStates.value).some((state) => state.locked && secondsLeft(state) <= 0)) return
  expiryQueryPending = true
  void loadLocks().finally(() => { expiryQueryPending = false })
}

function stopLockTimer(): void {
  if (lockTimer === undefined) return
  clearInterval(lockTimer)
  lockTimer = undefined
}

watch(anyLocked, (locked) => {
  if (!locked) stopLockTimer()
  else lockTimer ??= setInterval(tickLocks, 1000)
}, { flush: 'sync' })
onBeforeUnmount(stopLockTimer)

async function loadLocks(): Promise<void> {
  const token = lockLoad.next()
  lockFailed.value = false
  const emails = showLockColumn.value ? rows.value.map(emailOf).filter((e) => e !== '') : []
  if (emails.length === 0) {
    lockStates.value = {}
    lockLoaded.value = true
    return
  }
  try {
    const states = await loginLocksApi.query(emails)
    if (!lockLoad.isCurrent(token)) return
    lockQueriedAt.value = Date.now()
    lockNow.value = lockQueriedAt.value
    lockStates.value = Object.fromEntries(states.map((s) => [s.email, s]))
    lockLoaded.value = true
  } catch {
    if (!lockLoad.isCurrent(token)) return
    lockStates.value = {}
    lockFailed.value = true
    lockLoaded.value = true
  }
}

// Whole seconds as words for a tooltip: 59 s, 9 min 59 s, 1 h 2 min 5 s.
function spokenTime(seconds: number): string {
  const h = Math.floor(seconds / 3600)
  const m = Math.floor((seconds % 3600) / 60)
  const s = seconds % 60
  if (h > 0) return t('loginLocks.spokenHours', { h, m, s })
  if (m > 0) return t('loginLocks.spokenMinutes', { m, s })
  return t('loginLocks.spokenSeconds', { s })
}

// A super-admin account is the one a caller who is not a super-admin cannot clear; the same flag marks
// the row whose delete is refused to that caller.
function isProtectedRow(row: Row): boolean {
  return lockStates.value[emailOf(row)]?.clearable === false
}

// A super-admin role and the public role cannot be deleted by anyone; the reason, when the row is one.
function undeletableRoleHint(row: Row): string | undefined {
  if (name.value !== ROLE_COLLECTION) return undefined
  if (row.isSuperAdmin === true) return t('superAdmin.roleUndeletable')
  return row.name === PUBLIC_ROLE_NAME ? t('superAdmin.publicRoleUndeletable') : undefined
}

async function onUnlock(row: Row): Promise<void> {
  const email = emailOf(row)
  if (unlockingEmail.value !== null) return
  const header = t('loginLocks.confirmHeader')
  if (!(await confirm.require({ header, message: t('loginLocks.confirmMessage', { email }), severity: 'danger' }))) return
  unlockingEmail.value = email
  try {
    await loginLocksApi.clear(email)
    toast.add({ severity: 'success', summary: t('loginLocks.unlocked'), life: 3000 })
    await loadLocks()
  } catch (e) {
    toast.add({ severity: 'error', summary: t('loginLocks.unlockFailed'), detail: traceIdDetail(e), life: 3500 })
  } finally {
    unlockingEmail.value = null
  }
}

// A disabled button gets no pointer events, so a tooltip lives on the span around it.
function lockCell(row: Row) {
  if (lockFailed.value) {
    return h('span', { class: 'text-muted-foreground', title: t('loginLocks.unavailableHint'), 'data-testid': 'lock-unavailable' },
      t('loginLocks.unavailable'))
  }
  if (!lockLoaded.value) return h('span', { class: 'text-muted-foreground', 'data-testid': 'lock-pending' }, '…')
  const email = emailOf(row)
  const state = lockStates.value[email]
  if (!state?.locked) {
    return h(Button, {
      variant: 'outline', size: 'sm', disabled: true, class: 'text-muted-foreground', 'data-testid': 'lock-none',
      'aria-label': t('loginLocks.ariaNotLocked', { email }),
    }, () => t('loginLocks.notLocked'))
  }

  const left = secondsLeft(state)
  const clock = formatClock(left)
  const tip = t('loginLocks.lockedTip', { time: spokenTime(left) })
  if (canWrite.value && state.clearable) {
    const busy = unlockingEmail.value === email
    return h('span', { class: 'inline-flex', title: tip }, [h(Button, {
      variant: 'outline', size: 'sm', class: 'border-warning bg-warning/10', 'data-testid': 'lock-unlock',
      disabled: busy,
      'aria-label': t('loginLocks.ariaUnlock', { email, time: clock }),
      onClick: () => onUnlock(row),
    }, () => (busy ? t('loginLocks.unlocking') : t('loginLocks.unlockWithTime', { time: clock })))])
  }
  const reason = canWrite.value ? t('loginLocks.superAdminOnly') : t('loginLocks.readOnlyHint')
  // The tooltip sits on a span a keyboard or screen-reader user never reaches, so the reason, without
  // the countdown, is also a visually hidden description the button points at.
  const reasonId = `${lockReasonUid}-${primitiveText(row.id)}`
  return h('span', { class: 'inline-flex', title: t('loginLocks.tooltipWithReason', { tip, reason }) }, [
    h(Button, {
      variant: 'outline', size: 'sm', disabled: true, class: 'border-warning bg-warning/10', 'data-testid': 'lock-locked',
      'aria-label': t('loginLocks.ariaLocked', { email, time: clock }),
      'aria-describedby': reasonId,
    }, () => t('loginLocks.lockedWithTime', { time: clock })),
    h('span', { id: reasonId, class: 'sr-only' }, reason),
  ])
}

const lockReasonUid = `lock-reason-${useId()}`

function actionButtons(row: Row) {
  const buttons = []
  if (mode.value === 'active') {
    buttons.push(h(Button, {
      variant: 'ghost', size: 'icon', 'data-testid': canWrite.value ? 'row-edit' : 'row-view',
      title: canWrite.value ? t('collectionList.edit') : t('collectionList.view'),
      'aria-label': canWrite.value ? t('collectionList.edit') : t('collectionList.view'),
      onClick: () => onEdit(row),
    }, () => h(canWrite.value ? Pencil : Eye, { class: 'size-4' })))
    if (canDelete.value) {
      buttons.push(h(Button, {
        variant: 'ghost', size: 'icon', 'data-testid': 'row-delete',
        disabled: isProtectedRow(row) || undeletableRoleHint(row) !== undefined,
        title: undeletableRoleHint(row)
          ?? (isProtectedRow(row) ? t('superAdmin.deleteAccountHint') : t('collectionList.delete')),
        'aria-label': t('collectionList.delete'),
        onClick: () => onDelete(row),
      }, () => h(Trash2, { class: 'size-4 text-destructive' })))
    }
  } else if (canDelete.value) {
    buttons.push(
      h(Button, {
        variant: 'ghost', size: 'icon', 'data-testid': 'row-restore',
        title: t('collectionList.restore'), 'aria-label': t('collectionList.restore'),
        onClick: () => onRestore(row),
      }, () => h(Undo2, { class: 'size-4' })),
      h(Button, {
        variant: 'ghost', size: 'icon', 'data-testid': 'row-purge',
        title: t('collectionList.purge'), 'aria-label': t('collectionList.purge'),
        onClick: () => onPurge(row),
      }, () => h(Trash2, { class: 'size-4 text-destructive' })),
    )
  }
  return buttons
}

// TanStack columns, rebuilt from selectListColumns' picks plus the trash-only deletedAt column
// and the row-actions column; formatCell/cellValue/tagSeverity supply each cell's rendered value
// via a `cell` render function.
const tableColumns = computed<DataTableColumn<Row>[]>(() => {
  const cols: DataTableColumn<Row>[] = columns.value.map((c: ListColumn) => ({
    id: c.field,
    accessorKey: c.field,
    header: c.header,
    meta: { sortable: c.sortable },
    cell: ({ row }) => {
      const field = fieldOf(c.field)
      if (!field) return ''
      const value = cellValue(row.original, field)
      const text = formatCell(value, field)
      if (isSelectField(c.field) && value != null && value !== '') {
        const success = tagSeverity(value) === 'success'
        return h(Badge, {
          variant: 'outline',
          class: success ? 'border-success/40 bg-success/10 text-success' : 'text-muted-foreground',
        }, () => text)
      }
      return text
    },
  }))

  if (mode.value === 'trash') {
    cols.push({
      id: 'deletedAt',
      accessorKey: 'deletedAt',
      header: t('collectionList.deletedAt'),
      cell: ({ row }) => h('span', { class: 'tabular-nums text-muted-foreground' }, formatDeletedAt(row.original.deletedAt)),
    })
  }

  if (showLockColumn.value) {
    cols.push({
      id: 'loginLock',
      header: t('loginLocks.column'),
      cell: ({ row }) => lockCell(row.original),
    })
  }

  if (canRead.value || canDelete.value) {
    cols.push({
      id: 'actions',
      header: t('collectionList.actions'),
      cell: ({ row }) => h('div', { class: 'flex items-center gap-1' }, actionButtons(row.original)),
    })
  }

  return cols
})

// Latest-wins guard: a slow earlier load must not clobber a newer one's state.
const listLoad = createLatestWins()

async function loadItems(): Promise<void> {
  const token = listLoad.next()
  await schema.load() // dedup via store's `loaded` flag; retries on hard refresh/deep link where the
  // parent's schema.load() hasn't resolved yet when this view's onMounted(loadItems) fires
  if (!meta.value || !canRead.value) {
    // A bail still bumped the token above, so if a prior in-flight load turned the
    // spinner on, this call has orphaned it — its finally now sees a stale token and
    // will skip loading=false. Clear it here when we are the current load.
    if (listLoad.isCurrent(token)) loading.value = false
    return
  }
  loading.value = true
  error.value = ''
  lockStates.value = {}
  lockLoaded.value = false
  try {
    const sort = toSortParam(tableState.value.sort)
    await langStore.load()
    const res = await itemsApi.list(name.value, {
      page: tableState.value.page,
      rows: tableState.value.pageSize,
      sort,
      filter: Object.keys(filters.value).length ? filters.value : undefined,
      locale: langStore.defaultCode || undefined,
      deleted: mode.value === 'trash' ? 'only' : undefined,
    })
    if (!listLoad.isCurrent(token)) return
    rows.value = res.data
    total.value = res.total
    totalKnown.value = true
    void loadLocks()
  } catch (e) {
    if (!listLoad.isCurrent(token)) return
    error.value = e instanceof Error ? e.message : t('common.loadFailed')
    rows.value = []
    total.value = 0
    totalKnown.value = false
    lockStates.value = {}
  } finally {
    // Only the current load may clear the spinner; a stale response must not
    // turn off the spinner of the newer request still in flight.
    if (listLoad.isCurrent(token)) loading.value = false
  }
}

function onTableState(next: DataTableState): void {
  tableState.value = next
  void loadItems()
}
function onPageChange(page: number): void {
  tableState.value = { ...tableState.value, page }
  void loadItems()
}
function onPageSizeChange(pageSize: number): void {
  // An offset computed against the OLD page size is meaningless against the new one (e.g. page 3
  // at 25/page starts at row 75; at 100/page that's off the end of the result set) -- always
  // return to the first page when the size changes.
  tableState.value = { ...tableState.value, page: 0, pageSize }
  void loadItems()
}
function onFilterApply(spec: FilterSpec): void {
  filters.value = spec
  // A new filter invalidates the current offset.
  tableState.value = { ...tableState.value, page: 0 }
  void loadItems()
}

function onEdit(row: Row): void {
  const rid = primitiveText(row.id)
  if (rid !== '') void router.push({ name: 'collection-item', params: { name: name.value, id: rid } })
}

function onNew(): void {
  if (isUserCollection.value) {
    createUserOpen.value = true
    return
  }
  void router.push({ name: 'collection-create', params: { name: name.value } })
}

// The edit page replaces this view, so there is no list to refresh before navigating.
function onUserCreated(created: CreatedUser): void {
  void router.push({ name: 'collection-item', params: { name: USER_COLLECTION, id: created.id } })
}

function setMode(m: 'active' | 'trash'): void {
  mode.value = m
  tableState.value = { ...tableState.value, page: 0 }
  void loadItems()
}
function onModeToggle(value: unknown): void {
  // reka's single-type ToggleGroup emits undefined when the pressed item is clicked again
  // (deselect) -- the trash switch has no "neither" state, so ignore that.
  if (value === 'active' || value === 'trash') setMode(value)
}

function rowId(row: Row): string {
  return String(row.id)
}

async function runAction(fn: () => Promise<void>): Promise<void> {
  error.value = ''
  try {
    await fn()
    await loadItems()
    // Mutating a Language row changes which locale tabs every other form/list shows.
    if (name.value === LANGUAGE_COLLECTION) await langStore.reload()
  } catch (e) {
    error.value = writeFailureMessage(e, t, 'common.actionFailed')
  }
}

async function onDelete(row: Row): Promise<void> {
  const kind = deleteKindFor(meta.value)
  const { header, message } = deleteConfirm(t, kind)
  if (await confirm.require({ header, message, severity: 'danger' })) {
    await runAction(() => itemsApi.remove(name.value, rowId(row)))
  }
}

async function onPurge(row: Row): Promise<void> {
  const { header, message } = purgeConfirm(t)
  if (await confirm.require({ header, message, severity: 'danger' })) {
    await runAction(() => itemsApi.remove(name.value, rowId(row), { purge: true }))
  }
}

async function onRestore(row: Row): Promise<void> {
  await runAction(async () => { await itemsApi.restore(name.value, rowId(row)) })
}

onMounted(loadItems)

defineExpose({ loadItems, onTableState, onPageChange, onPageSizeChange, onFilterApply, onEdit, onNew,
  canWrite, canCreate, createUserOpen, onUserCreated, canDelete, mode, setMode, showTrashSwitch, onDelete, onRestore, onPurge, rows, total, totalKnown, loading,
  error, cellValue, tableState, filters, lockStates, lockFailed, onUnlock })
</script>

<template>
  <section class="collection-list">
    <template v-if="!meta">
      <p class="notice">{{ t('collectionList.notFound') }}</p>
    </template>
    <template v-else-if="!canRead">
      <p class="notice">{{ t('collectionList.noAccess') }}</p>
    </template>
    <template v-else>
      <PageHeader :title="meta.label" :caption="totalKnown ? t('collectionList.count', { n: total }) : undefined">
        <template #actions>
          <Button v-if="canCreate" @click="onNew">
            <Plus class="size-4" aria-hidden="true" />
            {{ t('collectionList.new') }}
          </Button>
        </template>
      </PageHeader>

      <div class="mb-4 flex flex-wrap items-start justify-between gap-3">
        <FilterBuilder :key="name" :fields="filterFields" :applied="filters" @apply="onFilterApply" />
        <ToggleGroup
          v-if="showTrashSwitch"
          type="single"
          :model-value="mode"
          variant="outline"
          @update:model-value="onModeToggle"
        >
          <ToggleGroupItem value="active">{{ t('collectionList.active') }}</ToggleGroupItem>
          <ToggleGroupItem value="trash">{{ t('collectionList.trash') }}</ToggleGroupItem>
        </ToggleGroup>
      </div>

      <p
        v-if="mode === 'trash'"
        role="status"
        class="mb-3 flex items-center gap-2 rounded-md border border-warning bg-warning/10 px-3.5 py-2.5 text-sm"
      >
        <Trash2 class="size-4" aria-hidden="true" />
        {{ t('collectionList.trashNotice') }}
      </p>

      <CreateUserDialog v-if="isUserCollection && canCreate" v-model:open="createUserOpen" @created="onUserCreated" />

      <p v-if="error" class="error" role="alert">{{ error }}</p>

      <DataTable
        :columns="tableColumns"
        :rows="rows"
        :total="total"
        :state="tableState"
        :loading="loading"
        :empty-message="t(mode === 'trash' ? 'collectionList.emptyTrash' : 'collectionList.empty')"
        @update:state="onTableState"
      />
      <DataTablePagination
        :page="tableState.page"
        :page-size="tableState.pageSize"
        :total="total"
        :ready="totalKnown"
        @update:page="onPageChange"
        @update:page-size="onPageSizeChange"
      />
    </template>
  </section>
</template>
