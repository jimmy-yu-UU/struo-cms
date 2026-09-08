import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'
import type { FormModel, RelationLink } from '../types/itemForm'
import { splitFields } from './splitFields'
import { relationInputKind } from './relationInputKind'
import { getFieldType } from './fieldTypes/registry'
import { usesLinksEditor, visiblePayloadFields, type ResolveCollection } from './junctionLinks'

// A deep-expanded M2M target row -> one RelationLink. `_junction` is absent when the junction
// carries no payload or the caller lacks the junction's read grant; every visible payload field
// is still seeded (via the registry's parse of undefined) so the editor has a stable key set.
function parseLink(row: Record<string, unknown>, fields: FieldMeta[]): RelationLink {
  const raw = (row._junction as Record<string, unknown> | undefined) ?? {}
  const junction: Record<string, unknown> = {}
  for (const f of fields) junction[f.name] = getFieldType(f.interface).parse(raw[f.name], f)
  return { id: String(row.id), junction }
}

export function parseItemToForm(
  meta: CollectionMeta,
  item: Record<string, unknown>,
  locales: LanguageInfo[],
  resolveCollection?: ResolveCollection,
): FormModel {
  const { shared, translatable } = splitFields(meta)
  const sharedModel: Record<string, unknown> = {}
  for (const f of shared) sharedModel[f.name] = getFieldType(f.interface).parse(item[f.name], f)

  const itemTranslations = (item.translations ?? {}) as Record<string, Record<string, unknown>>
  const translations: Record<string, Record<string, unknown>> = {}
  for (const loc of locales) {
    const src = itemTranslations[loc.code] ?? {}
    const entry: Record<string, unknown> = {}
    for (const f of translatable) entry[f.name] = getFieldType(f.interface).parse(src[f.name], f)
    translations[loc.code] = entry
  }
  const relations: Record<string, unknown> = {}
  for (const rel of meta.relations ?? []) {
    const kind = relationInputKind(rel.interface)
    if (kind === 'dropdown' || kind === 'treeSelect') {
      const nested = item[rel.name] as { id?: unknown } | null | undefined
      relations[rel.name] = nested?.id ?? null
    } else if (kind === 'tagSelect') {
      const arr = (item[rel.name] as Array<Record<string, unknown>> | undefined) ?? []
      // Without a resolver (callers that predate the links editor) the shape stays id[] verbatim.
      relations[rel.name] = resolveCollection && usesLinksEditor(rel, resolveCollection)
        ? arr.map((r) => parseLink(r, visiblePayloadFields(rel, resolveCollection)))
        : arr.map((r) => r.id)
    }
  }
  const version = typeof item.version === 'number' ? item.version : undefined
  return { shared: sharedModel, translations, relations, version }
}

export function blankItemForm(meta: CollectionMeta, locales: LanguageInfo[], resolveCollection?: ResolveCollection): FormModel {
  return parseItemToForm(meta, {}, locales, resolveCollection)
}
