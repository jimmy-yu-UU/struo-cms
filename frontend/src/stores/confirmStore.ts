import { defineStore } from 'pinia'

export type ConfirmRequest = {
  message: string
  header?: string
  acceptLabel?: string
  rejectLabel?: string
  severity?: 'primary' | 'danger'
}

type State = {
  open: boolean
  request: ConfirmRequest | null
  resolve: ((accepted: boolean) => void) | null
  requestId: number
}

// One dialog for the whole app. The previous PrimeVue setup mounted <ConfirmDialog /> in six
// components; when a parent and child were both mounted the same confirmation fired twice.
// Holding the single pending request here makes that structurally impossible.
export const useConfirmStore = defineStore('confirm', {
  state: (): State => ({ open: false, request: null, resolve: null, requestId: 0 }),
  actions: {
    ask(request: ConfirmRequest): Promise<boolean> {
      // Never strand a superseded request: its awaiter would hang forever.
      this.resolve?.(false)
      this.requestId += 1
      this.request = request
      this.open = true
      return new Promise<boolean>((resolve) => { this.resolve = resolve })
    },
    // `id`, when passed, is the id the caller captured for the request it meant to answer.
    // Omit it to always settle the current request (existing call sites, and tests, that
    // don't need the guard).
    accept(id?: number): void { this.settle(true, id) },
    reject(id?: number): void { this.settle(false, id) },
    // Ignores a call whose id no longer matches the pending request: a click already bound to
    // a superseded request must not be able to resolve the *new* one that replaced it.
    settle(accepted: boolean, id?: number): void {
      if (id !== undefined && id !== this.requestId) return
      const resolve = this.resolve
      this.open = false
      this.request = null
      this.resolve = null
      resolve?.(accepted)
    },
  },
})
