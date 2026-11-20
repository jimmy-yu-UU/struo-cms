// A field that parses its own input text (JSON) reports the text it could not parse as a draft, so
// the form can refuse to save, count it as an unsaved change and restore it when the field is
// mounted again. Drafts are keyed like the form's error map: `name`, or `locale:name` for a
// non-default locale's translatable field.
export type FieldDraft = { text: string; error: string }
export type FieldDrafts = Record<string, FieldDraft>

// A copy of `drafts` with `key` set to `draft`, or removed when `draft` is null.
export function withDraft(drafts: FieldDrafts, key: string, draft: FieldDraft | null): FieldDrafts {
  if (draft === null) {
    if (!Object.hasOwn(drafts, key)) return drafts
    const { [key]: _removed, ...rest } = drafts
    return rest
  }
  return { ...drafts, [key]: draft }
}

export function hasDrafts(drafts: FieldDrafts): boolean {
  return Object.keys(drafts).length > 0
}

// The form's error map entries for every draft.
export function draftErrors(drafts: FieldDrafts): Record<string, string> {
  return Object.fromEntries(Object.entries(drafts).map(([key, draft]) => [key, draft.error]))
}
