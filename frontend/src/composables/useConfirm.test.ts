import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useConfirm } from './useConfirm'
import { useConfirmStore } from '@/stores/confirmStore'

describe('useConfirm', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  it('forwards the request to the store and resolves with the outcome', async () => {
    const confirm = useConfirm()
    const store = useConfirmStore()
    const p = confirm.require({ message: 'Purge?', header: 'Purge', severity: 'danger' })
    expect(store.request).toEqual({ message: 'Purge?', header: 'Purge', severity: 'danger' })
    store.accept()
    await expect(p).resolves.toBe(true)
  })

  it('resolves false rather than rejecting when cancelled', async () => {
    const confirm = useConfirm()
    const store = useConfirmStore()
    const p = confirm.require({ message: 'Purge?' })
    store.reject()
    await expect(p).resolves.toBe(false)
  })
})
