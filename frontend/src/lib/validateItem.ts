import type { CollectionMeta } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'

function isEmpty(v: unknown): boolean {
  return v === undefined || v === null || v === ''
}

export function validateItem(
  meta: CollectionMeta,
  model: FormModel,
  defaultCode: string,
): Record<string, string> {
  const { shared, translatable } = splitFields(meta)
  const errors: Record<string, string> = {}
  for (const f of shared) {
    if (f.required && isEmpty(model.shared[f.name])) errors[f.name] = `${f.label} is required.`
  }
  const defaultValues = model.translations[defaultCode] ?? {}
  for (const f of translatable) {
    if (f.required && isEmpty(defaultValues[f.name])) errors[f.name] = `${f.label} is required.`
  }
  return errors
}
