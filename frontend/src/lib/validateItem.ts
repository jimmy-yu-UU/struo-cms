import type { CollectionMeta, FieldMeta, RelationMeta } from '../types/schema'
import type { FormModel, RelationLink } from '../types/itemForm'
import { isEmpty } from './fieldTypes/types'
import { splitFields } from './splitFields'
import { localeIsSent, translationErrorKey } from './translationErrors'
import { isRelationLinks, visiblePayloadFields, type ResolveCollection } from './junctionLinks'
import en from '../locales/en'

// Message translator injected by the caller (the UI passes vue-i18n's `t`), so this module stays a
// pure function with no i18n instance of its own.
export type Translate = (key: string, params?: Record<string, string | number>) => string

// Used when no translator is passed: renders the English pack's template with `{name}` params.
const englishTranslate: Translate = (key, params = {}) => {
  const template = key.split('.').reduce<unknown>(
    (node, part) => (node as Record<string, unknown> | undefined)?.[part], en)
  if (typeof template !== 'string') return key
  return template.replace(/\{(\w+)\}/g, (_, name: string) => String(params[name] ?? ''))
}

function tooLong(f: { maxLength?: number | null }, v: unknown): boolean {
  return typeof v === 'string' && f.maxLength != null && v.length > f.maxLength
}

// One message per relation (ItemForm shows a single FieldError under the relation), first violation
// wins, row numbers are 1-based to match what the user sees.
function firstLinkError(
  rel: RelationMeta,
  links: RelationLink[],
  fields: FieldMeta[],
  t: Translate,
): string | null {
  for (const [i, link] of links.entries()) {
    for (const f of fields) {
      const v = link.junction[f.name]
      const params = { relation: rel.label, label: f.label, max: f.maxLength ?? '', row: i + 1 }
      if (f.required && isEmpty(v, f)) return t('validation.rowRequired', params)
      if (tooLong(f, v)) return t('validation.rowMaxLength', params)
    }
  }
  return null
}

// Shared per-field precedence (required first, then maxLength), used for both the shared and the
// translatable field loops in validateItem.
function validateFields(
  fields: FieldMeta[],
  values: Record<string, unknown>,
  errors: Record<string, string>,
  t: Translate,
  keyOf: (name: string) => string = (name) => name,
): void {
  for (const f of fields) {
    if (f.required && isEmpty(values[f.name], f)) errors[keyOf(f.name)] = t('validation.required', { label: f.label })
    else if (tooLong(f, values[f.name])) {
      errors[keyOf(f.name)] = t('validation.maxLength', { label: f.label, max: f.maxLength ?? '' })
    }
  }
}

// The default locale is always validated; every other locale only when it carries content, i.e. when
// buildItemPayload would send it.
function validateTranslations(
  translatable: FieldMeta[],
  model: FormModel,
  defaultCode: string,
  errors: Record<string, string>,
  t: Translate,
): void {
  validateFields(translatable, model.translations[defaultCode] ?? {}, errors, t)
  for (const [code, values] of Object.entries(model.translations)) {
    if (code === defaultCode || !localeIsSent(translatable, values)) continue
    validateFields(translatable, values, errors, t, (name) => translationErrorKey(code, name, defaultCode))
  }
}

function validateLinks(
  meta: CollectionMeta,
  model: FormModel,
  resolveCollection: ResolveCollection,
  canWriteJunction: ((rel: RelationMeta) => boolean) | undefined,
  errors: Record<string, string>,
  t: Translate,
): void {
  for (const rel of meta.relations ?? []) {
    const v = model.relations[rel.name]
    if (!isRelationLinks(v)) continue
    // A relation whose junction the caller cannot write sends bare ids regardless of what the
    // (possibly disabled/hidden) payload inputs currently hold — validating that unsent payload
    // would block a save the user has no way to fix. Without `canWriteJunction`, behaviour is
    // unchanged: every relation is validated (existing callers/tests).
    if (canWriteJunction && !canWriteJunction(rel)) continue
    const err = firstLinkError(rel, v, visiblePayloadFields(rel, resolveCollection), t)
    if (err) errors[rel.name] = err
  }
}

export function validateItem(
  meta: CollectionMeta,
  model: FormModel,
  defaultCode: string,
  resolveCollection?: ResolveCollection,
  canWriteJunction?: (rel: RelationMeta) => boolean,
  t: Translate = englishTranslate,
): Record<string, string> {
  const { shared, translatable } = splitFields(meta)
  const errors: Record<string, string> = {}
  validateFields(shared, model.shared, errors, t)
  validateTranslations(translatable, model, defaultCode, errors, t)
  if (resolveCollection) validateLinks(meta, model, resolveCollection, canWriteJunction, errors, t)
  return errors
}
