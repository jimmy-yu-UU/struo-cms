// A field that parses its own input text (JSON) reports the text it could not parse as a draft, so
// the form can refuse to save, count it as an unsaved change and restore it when the field is
// mounted again. Drafts are keyed like the form's error map: `name`, or `locale:name` for a
// non-default locale's translatable field. A payload field of a many-to-many link has a nested key
// (relation, link id, field) that no form field name can equal.
export type FieldDraft = { text: string; error: string }
export type FieldDrafts = Record<string, FieldDraft>

const SEPARATOR = '\u001f'

// A copy of `drafts` with `key` set to `draft`, or removed when `draft` is null.
export function withDraft(drafts: FieldDrafts, key: string, draft: FieldDraft | null): FieldDrafts {
  if (draft !== null) return { ...drafts, [key]: draft }
  if (!Object.hasOwn(drafts, key)) return drafts
  const rest = { ...drafts }
  delete rest[key]
  return rest
}

export function hasDrafts(drafts: FieldDrafts): boolean {
  return Object.keys(drafts).length > 0
}

export function linkDraftKey(relation: string, linkId: string, field: string): string {
  return [relation, linkId, field].join(SEPARATOR)
}

// The prefix shared by every draft key of one link.
export function linkDraftPrefix(relation: string, linkId: string): string {
  return [relation, linkId, ''].join(SEPARATOR)
}

// The relation a nested draft key belongs to, or null for a form field's key.
export function relationOfDraftKey(key: string): string | null {
  const at = key.indexOf(SEPARATOR)
  return at < 0 ? null : key.slice(0, at)
}

// The drafts of form fields (not of link payload fields), as the form's error map entries.
export function fieldDraftErrors(drafts: FieldDrafts): Record<string, string> {
  return Object.fromEntries(
    Object.entries(drafts)
      .filter(([key]) => relationOfDraftKey(key) === null)
      .map(([key, draft]) => [key, draft.error]))
}

// The relations that hold at least one unparsable payload field.
export function relationsWithDrafts(drafts: FieldDrafts): Set<string> {
  const names = Object.keys(drafts).map(relationOfDraftKey)
  return new Set(names.filter((name): name is string => name !== null))
}
