import { useConfirmStore, type ConfirmRequest } from '@/stores/confirmStore'

export type { ConfirmRequest }

/**
 * Imperative confirmation, shaped like PrimeVue's useConfirm() so existing call sites move
 * with minimal edits — except this returns a Promise<boolean> instead of taking an
 * `accept` callback, which reads better at the call site:
 *
 *   if (await confirm.require({ message, header })) await doTheThing()
 *
 * Resolves false on cancel / dismiss. It never rejects, so callers need no try/catch.
 */
export function useConfirm(): { require(request: ConfirmRequest): Promise<boolean> } {
  const store = useConfirmStore()
  return { require: (request) => store.ask(request) }
}
