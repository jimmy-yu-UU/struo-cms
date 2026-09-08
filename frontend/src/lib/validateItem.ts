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

// Shared per-field precedence (required first, then maxLength), used for both the shared and the
// translatable field loops in validateItem.
function validateFields(fields: FieldMeta[], values: Record<string, unknown>, errors: Record<string, string>): void {
  for (const f of fields) {
    if (f.required && isEmpty(values[f.name])) errors[f.name] = `${f.label} is required.`
    else if (tooLong(f, values[f.name])) errors[f.name] = `${f.label} must be at most ${f.maxLength} characters.`
  }
}

function validateLinks(
  meta: CollectionMeta,
  model: FormModel,
  resolveCollection: ResolveCollection,
  canWriteJunction: ((rel: RelationMeta) => boolean) | undefined,
  errors: Record<string, string>,
): void {
  for (const rel of meta.relations ?? []) {
    const v = model.relations[rel.name]
    if (!isRelationLinks(v)) continue
    // A relation whose junction the caller cannot write sends bare ids regardless of what the
    // (possibly disabled/hidden) payload inputs currently hold — validating that unsent payload
    // would block a save the user has no way to fix. Without `canWriteJunction`, behaviour is
    // unchanged: every relation is validated (existing callers/tests).
    if (canWriteJunction && !canWriteJunction(rel)) continue
    const err = firstLinkError(rel, v, visiblePayloadFields(rel, resolveCollection))
    if (err) errors[rel.name] = err
  }
}

export function validateItem(
  meta: CollectionMeta,
  model: FormModel,
  defaultCode: string,
  resolveCollection?: ResolveCollection,
  canWriteJunction?: (rel: RelationMeta) => boolean,
): Record<string, string> {
  const { shared, translatable } = splitFields(meta)
  const errors: Record<string, string> = {}
  validateFields(shared, model.shared, errors)
  const defaultValues = model.translations[defaultCode] ?? {}
  validateFields(translatable, defaultValues, errors)
  if (resolveCollection) validateLinks(meta, model, resolveCollection, canWriteJunction, errors)
  return errors
}
