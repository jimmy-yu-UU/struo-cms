import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'
import { relationInputKind } from './relationInputKind'

export function parseItemToForm(
  meta: CollectionMeta,
  item: Record<string, unknown>,
  locales: LanguageInfo[],
): FormModel {
  const { shared, translatable } = splitFields(meta)
  const sharedModel: Record<string, unknown> = {}
  for (const f of shared) {
    // File/image fields hold a scalar Guid? FK; default to null (never '') so a
    // resave without touching the field can't emit an invalid empty-string uuid.
    const empty = f.interface === 'file' || f.interface === 'image' ? null : ''
    sharedModel[f.name] = item[f.name] ?? empty
  }

  const itemTranslations = (item.translations ?? {}) as Record<string, Record<string, unknown>>
  const translations: Record<string, Record<string, unknown>> = {}
  for (const loc of locales) {
    const src = itemTranslations[loc.code] ?? {}
    const entry: Record<string, unknown> = {}
    for (const f of translatable) entry[f.name] = src[f.name] ?? ''
    translations[loc.code] = entry
  }
  const relations: Record<string, unknown> = {}
  for (const rel of meta.relations ?? []) {
    const kind = relationInputKind(rel.interface)
    if (kind === 'dropdown' || kind === 'treeSelect') {
      const nested = item[rel.name] as { id?: unknown } | null | undefined
      relations[rel.name] = nested?.id ?? null
    } else if (kind === 'tagSelect') {
      const arr = (item[rel.name] as Array<{ id?: unknown }> | undefined) ?? []
      relations[rel.name] = arr.map((r) => r.id)
    }
  }
  const version = typeof item.version === 'number' ? item.version : undefined
  return { shared: sharedModel, translations, relations, version }
}

export function blankItemForm(meta: CollectionMeta, locales: LanguageInfo[]): FormModel {
  return parseItemToForm(meta, {}, locales)
}
