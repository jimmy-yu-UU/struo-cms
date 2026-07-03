import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { FormModel } from '../types/itemForm'
import { splitFields } from './splitFields'
import { relationInputKind } from './relationInputKind'

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

function isEmpty(v: unknown): boolean {
  return v === undefined || v === null || v === ''
}

export function buildItemPayload(
  meta: CollectionMeta,
  model: FormModel,
  locales: LanguageInfo[],
  mode: 'create' | 'update',
): Record<string, unknown> {
  const { shared, translatable } = splitFields(meta)
  const payload: Record<string, unknown> = {}

  for (const f of shared) {
    const v = model.shared[f.name]
    if (mode === 'update' || !isEmpty(v)) payload[f.name] = v
  }

  const defaultCode = locales.find((l) => l.isDefault)?.code
  const translations: Record<string, Record<string, unknown>> = {}
  for (const loc of locales) {
    const values = model.translations[loc.code] ?? {}
    const hasContent = translatable.some((f) => !isEmpty(values[f.name]))
    const isDefault = loc.code === defaultCode
    // create: always send default locale; else only when it has content.
    // update: send only locales the user actually filled.
    if (mode === 'create' && !isDefault && !hasContent) continue
    if (mode === 'update' && !hasContent) continue
    const entry: Record<string, unknown> = {}
    for (const f of translatable) entry[f.name] = values[f.name]
    translations[loc.code] = entry
  }
  const relations = model.relations ?? {}
  for (const rel of meta.relations ?? []) {
    if (!(rel.name in relations)) continue // untouched -> partial update
    const kind = relationInputKind(rel.interface)
    if (kind === 'dropdown' || kind === 'treeSelect') {
      if (rel.foreignKey) payload[camel(rel.foreignKey)] = relations[rel.name] ?? null
    } else if (kind === 'tagSelect') {
      payload[rel.name] = relations[rel.name] ?? []
    }
    // relatedList / readonly: never written
  }

  if (Object.keys(translations).length > 0) payload.translations = translations
  return payload
}
