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
// Button is a functional stub (not `true`) so click handlers wired to it (e.g. the retry button) are exercisable.
const stubs = {
  Drawer: { template: '<div class="drawer"><slot /></div>' },
  Button: {
    props: ['label', 'size', 'text', 'icon', 'severity'],
    emits: ['click'],
    template: '<button class="stub-btn" @click="$emit(\'click\')"><slot/>{{ label }}</button>',
  },
  ConfirmDialog: true,
}

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

  it('retry button re-loads the list after a failed load', async () => {
    const list = vi.spyOn(itemsApi, 'listRevisions').mockRejectedValue(new Error('net'))
    const w = mountDrawer()
    await w.vm.load()
    expect((w.vm as any).listError).toBe('Failed to load revisions')
    const retryBtn = w.find('.rev-notice.rev-error .stub-btn')
    expect(retryBtn.exists()).toBe(true)

    list.mockResolvedValue(rows)
    const callsBefore = list.mock.calls.length
    await retryBtn.trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))
    await w.vm.$nextTick()

    expect(list.mock.calls.length).toBeGreaterThan(callsBefore)
    expect((w.vm as any).listError).toBe('')
    expect((w.vm as any).revisions).toHaveLength(2)
    expect(w.text()).not.toContain('Failed to load revisions')
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

  it('select is latest-wins: a slow stale response must not clobber a newer selection', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows)

    type Deferred<T> = { promise: Promise<T>; resolve: (v: T) => void }
    function deferred<T>(): Deferred<T> {
      let resolve!: (v: T) => void
      const promise = new Promise<T>((r) => { resolve = r })
      return { promise, resolve }
    }

    const detailA = { ...rows[0], snapshot: { status: 'A' } } // revisionNumber 2, selected first
    const detailB = { ...rows[1], snapshot: { status: 'B' } } // revisionNumber 1, selected second (latest)
    const dA = deferred<typeof detailA>()
    const dB = deferred<typeof detailB>()
    vi.spyOn(itemsApi, 'getRevision').mockImplementation((_c, _i, n) => {
      return n === rows[0].revisionNumber ? dA.promise : dB.promise
    })

    const w = mountDrawer()
    await w.vm.load()

    // Click A, then quickly click B before A's response arrives.
    const selectA = (w.vm as any).select(rows[0])
    const selectB = (w.vm as any).select(rows[1])

    // B (the latest click) resolves first; A (the stale, earlier click) resolves after.
    dB.resolve(detailB)
    await selectB
    await w.vm.$nextTick()
    dA.resolve(detailA)
    await selectA
    await w.vm.$nextTick()

    expect((w.vm as any).selected).toEqual(rows[1])
    expect((w.vm as any).detail).toEqual(detailB)
    expect((w.vm as any).detailLoading).toBe(false)
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
