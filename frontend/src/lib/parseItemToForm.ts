import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'

export function parseItemToForm(
  meta: CollectionMeta,
  item: Record<string, unknown>,
  locales: LanguageInfo[],
): FormModel {
  const { shared, translatable } = splitFields(meta)
  const sharedModel: Record<string, unknown> = {}
  for (const f of shared) sharedModel[f.name] = item[f.name] ?? ''

  const itemTranslations = (item.translations ?? {}) as Record<string, Record<string, unknown>>
  const translations: Record<string, Record<string, unknown>> = {}
  for (const loc of locales) {
    const src = itemTranslations[loc.code] ?? {}
    const entry: Record<string, unknown> = {}
    for (const f of translatable) entry[f.name] = src[f.name] ?? ''
    translations[loc.code] = entry
  }
  return { shared: sharedModel, translations, relations: {} }
}

export function blankItemForm(meta: CollectionMeta, locales: LanguageInfo[]): FormModel {
  return parseItemToForm(meta, {}, locales)
}
