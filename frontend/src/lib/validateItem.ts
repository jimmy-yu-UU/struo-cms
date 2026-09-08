import type { CollectionMeta, FieldMeta, RelationMeta } from '../types/schema'
import type { FormModel, RelationLink } from '../types/itemForm'
import { splitFields } from './splitFields'
import { isRelationLinks, visiblePayloadFields, type ResolveCollection } from './junctionLinks'

function isEmpty(v: unknown): boolean {
  return v === undefined || v === null || v === ''
}

function tooLong(f: { maxLength?: number | null }, v: unknown): boolean {
  return typeof v === 'string' && f.maxLength != null && v.length > f.maxLength
}

// One message per relation (ItemForm shows a single FieldError under the relation), first violation
// wins, row numbers are 1-based to match what the user sees.
function firstLinkError(rel: RelationMeta, links: RelationLink[], fields: FieldMeta[]): string | null {
  for (const [i, link] of links.entries()) {
    for (const f of fields) {
      const v = link.junction[f.name]
      if (f.required && isEmpty(v)) return `${rel.label} › ${f.label} is required (row ${i + 1}).`
      if (tooLong(f, v)) return `${rel.label} › ${f.label} must be at most ${f.maxLength} characters (row ${i + 1}).`
    }
  }
  return null
}

export function validateItem(
  meta: CollectionMeta,
  model: FormModel,
  defaultCode: string,
  resolveCollection?: ResolveCollection,
): Record<string, string> {
  const { shared, translatable } = splitFields(meta)
  const errors: Record<string, string> = {}
  for (const f of shared) {
    if (f.required && isEmpty(model.shared[f.name])) errors[f.name] = `${f.label} is required.`
    else if (tooLong(f, model.shared[f.name])) errors[f.name] = `${f.label} must be at most ${f.maxLength} characters.`
  }
  const defaultValues = model.translations[defaultCode] ?? {}
  for (const f of translatable) {
    if (f.required && isEmpty(defaultValues[f.name])) errors[f.name] = `${f.label} is required.`
    else if (tooLong(f, defaultValues[f.name])) errors[f.name] = `${f.label} must be at most ${f.maxLength} characters.`
  }
  if (resolveCollection) {
    for (const rel of meta.relations ?? []) {
      const v = model.relations[rel.name]
      if (!isRelationLinks(v)) continue
      const err = firstLinkError(rel, v, visiblePayloadFields(rel, resolveCollection))
      if (err) errors[rel.name] = err
    }
  }
  return errors
}
