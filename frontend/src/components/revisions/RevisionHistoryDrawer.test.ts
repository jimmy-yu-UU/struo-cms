import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RevisionHistoryDrawer from './RevisionHistoryDrawer.vue'
import { itemsApi } from '../../api/itemsApi'

const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
const toastAdd = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { revisions: {
    title: 'Revision history', loading: 'Loading…', loadError: 'Failed to load revisions',
    retry: 'Retry', empty: 'No revisions yet', colWhen: 'Time', colWho: 'By', snapshot: 'Snapshot',
    opCreate: 'Created', opUpdate: 'Updated', opRevert: 'Reverted', opUnknown: 'Changed', system: 'System',
    revert: 'Revert to this revision', revertConfirmHeader: 'Confirm revert',
    revertConfirmMessage: 'Revert to {n}?', reverted: 'Reverted to {n}', revertFailed: 'Revert failed',
    detailError: 'Failed to load this revision', selectHint: 'Select a revision',
  } } },
})
// Stub Drawer so its content always renders (teleport/visible internals are not under test).
const stubs = { Drawer: { template: '<div class="drawer"><slot /></div>' }, Button: true, ConfirmDialog: true }

type DrawerProps = { visible?: boolean; collection?: string; itemId?: string; canRevert?: boolean }

function mountDrawer(props: DrawerProps = {}) {
  return mount(RevisionHistoryDrawer, {
    props: { visible: true, collection: 'article', itemId: '5', canRevert: true, ...props },
    global: { plugins: [i18n], stubs },
  })
}

const rows = [
  { revisionNumber: 2, operation: 'update', createdAt: '2026-07-21T02:00:00Z', createdBy: 'u1' },
  { revisionNumber: 1, operation: 'create', createdAt: '2026-07-21T01:00:00Z', createdBy: 'u1' },
]

describe('RevisionHistoryDrawer', () => {
  beforeEach(() => { vi.clearAllMocks() })

  it('loads the list when opened and renders rows', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows)
    const w = mountDrawer()
    await w.vm.load()
    expect(itemsApi.listRevisions).toHaveBeenCalledWith('article', '5')
    expect((w.vm as any).revisions).toHaveLength(2)
  })

  it('shows the empty state when there are no revisions', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue([])
    const w = mountDrawer()
    await w.vm.load()
    expect((w.vm as any).revisions).toHaveLength(0)
    expect(w.text()).toContain('No revisions yet')
  })

  it('records a list error when loading fails', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockRejectedValue(new Error('net'))
    const w = mountDrawer()
    await w.vm.load()
    expect((w.vm as any).listError).toBe('Failed to load revisions')
  })

  it('select loads the detail for a revision', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows)
    const detail = { ...rows[0], snapshot: { status: 'draft' } }
    const spy = vi.spyOn(itemsApi, 'getRevision').mockResolvedValue(detail)
    const w = mountDrawer()
    await w.vm.load()
    await (w.vm as any).select(rows[0])
    expect(spy).toHaveBeenCalledWith('article', '5', 2)
    expect((w.vm as any).detail).toEqual(detail)
  })

  it('onRevert confirms, reverts, emits reverted, reloads, and toasts success', async () => {
    const list = vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows)
    vi.spyOn(itemsApi, 'revert').mockResolvedValue({ id: '5', status: 'draft' })
    const w = mountDrawer()
    await w.vm.load()
    list.mockClear()
    ;(w.vm as any).onRevert(2)
    expect(confirmRequire).toHaveBeenCalled()
    await confirmRequire.mock.calls[0][0].accept()
    expect(itemsApi.revert).toHaveBeenCalledWith('article', '5', 2)
    expect(w.emitted('reverted')?.[0]).toEqual([{ id: '5', status: 'draft' }])
    expect(list).toHaveBeenCalledTimes(1) // reloaded after revert
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }))
  })

  it('onRevert toasts an error and does not emit when revert fails', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows)
    vi.spyOn(itemsApi, 'revert').mockRejectedValue(new Error('nope'))
    const w = mountDrawer()
    await w.vm.load()
    ;(w.vm as any).onRevert(2)
    await confirmRequire.mock.calls[0][0].accept()
    expect(w.emitted('reverted')).toBeUndefined()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }))
  })
})
