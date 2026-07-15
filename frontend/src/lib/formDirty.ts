import type { FormModel } from '../types/itemForm'

// Dirty-state tracking for item forms (FE-5).
//
// A form is "dirty" when the user's editable content differs from the last
// committed baseline. We snapshot the model as a deterministic JSON string and
// compare snapshots — FormModel is built by parse/setModel with a stable key
// order, so a plain JSON.stringify is a reliable, dependency-free comparison.
//
// `version` is deliberately excluded: it is an optimistic-concurrency token, not
// user content. During 409-conflict recovery the token is refreshed in place, and
// that refresh must never by itself flag the form as dirty (FE-4 interplay).
export function snapshotModel(model: FormModel): string {
  return JSON.stringify({
    shared: model.shared,
    translations: model.translations,
    relations: model.relations,
  })
}

export function isDirty(baseline: string, model: FormModel): boolean {
  return snapshotModel(model) !== baseline
}

// Confirm copy for the leave guard — mirrors the pure-helper convention in
// deleteAction.ts. No i18n; English literals.
export function unsavedConfirm(): { header: string; message: string } {
  return {
    header: 'Unsaved changes',
    message: 'You have unsaved changes. Leave this page and discard them?',
  }
}
