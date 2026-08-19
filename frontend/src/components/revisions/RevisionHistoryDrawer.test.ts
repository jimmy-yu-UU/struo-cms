import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RevisionHistoryDrawer from './RevisionHistoryDrawer.vue'
import { itemsApi } from '../../api/itemsApi'

const confirmRequire = vi.fn<(req: unknown) => Promise<boolean>>(() => Promise.resolve(true))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { revisions: {
    title: 'Revision history', loading: 'Loading…', loadError: 'Failed to load revisions',
    retry: 'Retry', empty: 'No revisions yet', colWhen: 'Time', colWho: 'By', snapshot: 'Snapshot',
    opCreate: 'Created', opUpdate: 'Updated', opRevert: 'Reverted', opRevertFrom: 'Reverted (from #{n})',
    opUnknown: 'Changed', system: 'System',
    revert: 'Revert to this revision', revertConfirmHeader: 'Confirm revert',
    revertConfirmMessage: 'Revert to {n}?', reverted: 'Reverted to {n}', revertFailed: 'Revert failed',
    detailError: 'Failed to load this revision', selectHint: 'Select a revision', reverting: 'Reverting…',
  } } },
})

type DrawerProps = { visible?: boolean; collection?: string; itemId?: string; canRevert?: boolean }

function mountDrawer(props: DrawerProps = {}) {
  return mount(RevisionHistoryDrawer, {
    props: { visible: true, collection: 'article', itemId: '5', canRevert: true, ...props },
    global: {
      plugins: [i18n],
      // reka's own portal wrapper is itself named Teleport and collides with VTU's stub, dropping
      // the whole sheet body. Nothing here asserts against document.body.
      stubs: { teleport: true },
      renderStubDefaultSlot: true,
    },
  })
}

const rows = [
  { revisionNumber: 2, operation: 'update', createdAt: '2026-07-21T02:00:00Z', createdBy: 'u1' },
  { revisionNumber: 1, operation: 'create', createdAt: '2026-07-21T01:00:00Z', createdBy: 'u1' },
]

function mockRevisions(revisions: unknown[]) {
  return vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(revisions as never)
}

const iso = '2026-07-21T02:00:00Z'

describe('RevisionHistoryDrawer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    confirmRequire.mockReset()
    confirmRequire.mockResolvedValue(true)
  })

  it('loads the list when opened and renders rows', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    const w = mountDrawer()
    await flushPromises()
    expect(itemsApi.listRevisions).toHaveBeenCalledWith('article', '5')
    expect((w.vm as any).revisions).toHaveLength(2)
  })

  it('shows the empty state when there are no revisions', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue([] as never)
    const w = mountDrawer()
    await flushPromises()
    expect((w.vm as any).revisions).toHaveLength(0)
    expect(w.text()).toContain('No revisions yet')
  })

  it('records a list error when loading fails', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockRejectedValue(new Error('net'))
    const w = mountDrawer()
    await flushPromises()
    expect((w.vm as any).listError).toBe('Failed to load revisions')
  })

  it('select loads the detail for a revision', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    const detail = { ...rows[0], snapshot: { status: 'draft' } }
    const spy = vi.spyOn(itemsApi, 'getRevision').mockResolvedValue(detail as never)
    const w = mountDrawer()
    await flushPromises()
    await (w.vm as any).select(rows[0])
    expect(spy).toHaveBeenCalledWith('article', '5', 2)
    expect((w.vm as any).detail).toEqual(detail)
  })

  it('select is latest-wins: a slow stale response must not clobber a newer selection', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)

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
      return (n === rows[0].revisionNumber ? dA.promise : dB.promise) as never
    })

    const w = mountDrawer()
    await flushPromises()

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

  // Pins the `watch(() => props.visible, ...)` contract itself (rather than calling load()
  // directly as every test above does) — deleting the watch would leave this test red while
  // leaving all the others green.
  it('the visible watch loads on open and does not load while closed', async () => {
    const list = vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    const w = mountDrawer({ visible: false })
    await w.vm.$nextTick()
    expect(list).not.toHaveBeenCalled()

    await w.setProps({ visible: true })
    await w.vm.$nextTick()
    expect(list).toHaveBeenCalledWith('article', '5')
  })

  // Pins the `{ immediate: true }` option specifically: a drawer mounted already-open must load
  // without any prop change. Without immediate, mounting with visible:true would not fire the
  // watch, so this asserts the eager first run (no manual load()/setProps here).
  it('loads immediately when mounted already-open', async () => {
    const list = vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    mountDrawer({ visible: true })
    await Promise.resolve()
    expect(list).toHaveBeenCalledWith('article', '5')
  })

  it('renders a vendored sheet', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    const w = mountDrawer()
    await flushPromises()
    expect(w.find('[data-slot="sheet-content"]').exists()).toBe(true)
    expect(w.find('[data-slot="sheet-title"]').exists()).toBe(true)
  })

  it('closes through the sheet\'s own open change', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    const w = mountDrawer()
    await flushPromises()
    // reka fires this for Escape, the overlay and the built-in close button alike; the component's
    // published contract is still `visible` + update:visible.
    await w.findComponent({ name: 'Sheet' }).vm.$emit('update:open', false)
    expect(w.emitted('update:visible')).toEqual([[false]])
  })

  // Inbound-direction guard on the migrated Sheet's own `open` state: reka's DialogRoot unmounts
  // its content subtree while closed (unmountOnHide defaults true), so a mount that starts closed
  // never renders sheet-content — and only a genuinely controlled `:open` binding will render it
  // once `visible` flips to true after mount. A `:default-open="visible"` regression would still
  // pass the "renders when mounted open" test above (byte-identical first render) but fail here.
  it('reflects the visible prop into the sheet\'s open state, including after mount', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    const w = mountDrawer({ visible: false })
    await flushPromises()
    expect(w.find('[data-slot="sheet-content"]').exists()).toBe(false)

    await w.setProps({ visible: true })
    await flushPromises()
    expect(w.find('[data-slot="sheet-content"]').exists()).toBe(true)
  })

  it('reverts only when the confirmation resolves true, then toasts success', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    const revert = vi.spyOn(itemsApi, 'revert').mockResolvedValue({ id: '5' } as never)
    const w = mountDrawer()
    await flushPromises()
    confirmRequire.mockResolvedValueOnce(false)
    await (w.vm as unknown as { onRevert: (n: number) => Promise<void> }).onRevert(2)
    expect(revert).not.toHaveBeenCalled()
    confirmRequire.mockResolvedValueOnce(true)
    await (w.vm as unknown as { onRevert: (n: number) => Promise<void> }).onRevert(2)
    await flushPromises()
    expect(revert).toHaveBeenCalledWith('article', '5', 2)
    expect(w.emitted('reverted')).toBeTruthy()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'success' }))
    expect(confirmRequire.mock.calls[0][0]).not.toHaveProperty('group')
  })

  it('onRevert toasts an error and does not emit when revert fails', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    vi.spyOn(itemsApi, 'revert').mockRejectedValue(new Error('nope'))
    const w = mountDrawer()
    await flushPromises()
    await (w.vm as unknown as { onRevert: (n: number) => Promise<void> }).onRevert(2)
    expect(w.emitted('reverted')).toBeUndefined()
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }))
  })

  // Every revert test above calls `onRevert` directly on the exposed vm, which cannot detect a
  // severed `@revert` binding in the template. This drives the actual child emit instead, so
  // deleting the binding in the template fails this test specifically.
  it('wires RevisionSnapshotView\'s real revert emit to onRevert', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    const detail = { ...rows[0], snapshot: { status: 'draft' } }
    vi.spyOn(itemsApi, 'getRevision').mockResolvedValue(detail as never)
    const revert = vi.spyOn(itemsApi, 'revert').mockResolvedValue({ id: '5' } as never)
    const w = mountDrawer()
    await flushPromises()
    await (w.vm as any).select(rows[0])
    await flushPromises()

    await w.findComponent({ name: 'RevisionSnapshotView' }).vm.$emit('revert', 2)
    await flushPromises()

    expect(revert).toHaveBeenCalledWith('article', '5', 2)
    expect(w.emitted('reverted')).toBeTruthy()
  })

  it('mounts no local confirmation dialog', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockResolvedValue(rows as never)
    const w = mountDrawer()
    await flushPromises()
    // The store-backed host in AppShell is the only confirmation host in the app, so this
    // component must not mount its own.
    expect(w.findComponent({ name: 'AlertDialog' }).exists()).toBe(false)
  })

  it('types every button in the drawer', async () => {
    vi.spyOn(itemsApi, 'listRevisions').mockRejectedValue(new Error('nope'))
    const w = mountDrawer()
    await flushPromises()
    const buttons = w.findAll('button')
    expect(buttons.length).toBeGreaterThan(0)
    buttons.forEach((b) => expect(b.attributes('type')).toBe('button'))
  })

  it('retries the list load from the error state through a vendored button', async () => {
    const list = vi.spyOn(itemsApi, 'listRevisions').mockRejectedValue(new Error('nope'))
    const w = mountDrawer()
    await flushPromises()
    const retryBtn = w.get('[data-test="rev-retry"]')
    // Identity guard: type="button" and a forwarded data-test attribute are not distinctive —
    // only the vendored component's own data-slot hook is.
    expect(retryBtn.attributes('data-slot')).toBe('button')
    expect(retryBtn.attributes('data-variant')).toBe('ghost')
    expect(retryBtn.attributes('data-size')).toBe('sm')
    await retryBtn.trigger('click')
    await flushPromises()
    expect(list).toHaveBeenCalledTimes(2)
  })

  it('annotates a revert node with the revision it restored', async () => {
    mockRevisions([
      { revisionNumber: 3, operation: 'revert', createdAt: iso, createdBy: null, sourceRevisionNumber: 1 },
      { revisionNumber: 2, operation: 'update', createdAt: iso, createdBy: null, sourceRevisionNumber: null },
      { revisionNumber: 1, operation: 'create', createdAt: iso, createdBy: null, sourceRevisionNumber: null },
    ])
    const w = mountDrawer()
    await flushPromises()
    const items = w.findAll('.rev-item__op')
    expect(items[0].text()).toBe('Reverted (from #1)')
    expect(items[1].text()).toBe('Updated')
  })

  it('falls back to the plain label for a revert with no recorded source', async () => {
    mockRevisions([
      { revisionNumber: 2, operation: 'revert', createdAt: iso, createdBy: null, sourceRevisionNumber: null },
      { revisionNumber: 1, operation: 'create', createdAt: iso, createdBy: null, sourceRevisionNumber: null },
    ])
    const w = mountDrawer()
    await flushPromises()
    expect(w.findAll('.rev-item__op')[0].text()).toBe('Reverted')
  })
})
