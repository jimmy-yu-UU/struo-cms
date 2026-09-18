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

// One dialog for the whole app: every caller shares this single pending request instead of each
// component mounting its own <ConfirmDialog />, which would let a parent and child both fire the
// same confirmation twice — sharing one request here makes that structurally impossible.
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
    // `id`, when passed, is the id a caller captured from `requestId` at the time it asked —
    // useful for a caller that holds onto that id across an await and might still call
    // accept()/reject() after a later ask() has superseded its request. Omit it (as
    // ConfirmHost's rendered buttons do — see the comment there) to always settle whatever is
    // current.
    accept(id?: number): void { this.settle(true, id) },
    reject(id?: number): void { this.settle(false, id) },
    // Ignores a call whose id no longer matches the pending request, narrative-guard:allow: describes the stale-id guard clause itself, not history
    // so a programmatic caller that captured an id before a supersede can't resolve the *new*
    // request that replaced it.
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
