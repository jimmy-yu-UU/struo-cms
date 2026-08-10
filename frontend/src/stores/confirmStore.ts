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
}

// One dialog for the whole app. The previous PrimeVue setup mounted <ConfirmDialog /> in six
// components; when a parent and child were both mounted the same confirmation fired twice.
// Holding the single pending request here makes that structurally impossible.
export const useConfirmStore = defineStore('confirm', {
  state: (): State => ({ open: false, request: null, resolve: null }),
  actions: {
    ask(request: ConfirmRequest): Promise<boolean> {
      // Never strand a superseded request: its awaiter would hang forever.
      this.resolve?.(false)
      this.request = request
      this.open = true
      return new Promise<boolean>((resolve) => { this.resolve = resolve })
    },
    accept(): void { this.settle(true) },
    reject(): void { this.settle(false) },
    settle(accepted: boolean): void {
      const resolve = this.resolve
      this.open = false
      this.request = null
      this.resolve = null
      resolve?.(accepted)
    },
  },
})
