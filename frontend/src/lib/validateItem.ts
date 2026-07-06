import type { CollectionMeta } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'

function isEmpty(v: unknown): boolean {
  return v === undefined || v === null || v === ''
}

function tooLong(f: { maxLength?: number | null }, v: unknown): boolean {
  return typeof v === 'string' && f.maxLength != null && v.length > f.maxLength
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
    else if (tooLong(f, model.shared[f.name])) errors[f.name] = `${f.label} must be at most ${f.maxLength} characters.`
  }
  const defaultValues = model.translations[defaultCode] ?? {}
  for (const f of translatable) {
    if (f.required && isEmpty(defaultValues[f.name])) errors[f.name] = `${f.label} is required.`
    else if (tooLong(f, defaultValues[f.name])) errors[f.name] = `${f.label} must be at most ${f.maxLength} characters.`
  }
  return errors
}
