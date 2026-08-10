import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useConfirmStore } from './confirmStore'

describe('confirmStore', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('starts closed', () => {
    expect(useConfirmStore().open).toBe(false)
    expect(useConfirmStore().request).toBeNull()
  })

  it('opens with the request and resolves true on accept', async () => {
    const store = useConfirmStore()
    const p = store.ask({ message: 'Delete this?' })
    expect(store.open).toBe(true)
    expect(store.request?.message).toBe('Delete this?')
    store.accept()
    await expect(p).resolves.toBe(true)
    expect(store.open).toBe(false)
  })

  it('resolves false on reject', async () => {
    const store = useConfirmStore()
    const p = store.ask({ message: 'Delete this?' })
    store.reject()
    await expect(p).resolves.toBe(false)
    expect(store.open).toBe(false)
  })

  // A second ask() while one is open must not strand the first promise forever —
  // an unresolved promise here means a caller's `await` never returns.
  it('resolves a superseded request as false', async () => {
    const store = useConfirmStore()
    const first = store.ask({ message: 'First' })
    const second = store.ask({ message: 'Second' })
    await expect(first).resolves.toBe(false)
    expect(store.request?.message).toBe('Second')
    store.accept()
    await expect(second).resolves.toBe(true)
  })
})
