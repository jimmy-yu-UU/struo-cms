import { useConfirmStore } from './confirmStore'
import { useLanguageStore } from './languageStore'
import { useSchemaStore } from './schemaStore'

// Forgets everything the previous session loaded, so the next sign-in starts clean. Theme, UI
// locale and the public app config are per-browser, not per-user, and stay. The auth store clears
// its own user.
export function resetSessionStores(): void {
  useSchemaStore().reset()
  useLanguageStore().reset()
  // A dialog left open from the old session would otherwise outlive it.
  useConfirmStore().reject()
}
