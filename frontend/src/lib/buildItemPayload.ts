import type { CollectionMeta, LanguageInfo, RelationMeta } from '../types/schema'
import type { FormModel, RelationLink } from '../types/itemForm'
import { splitFields } from './splitFields'
import { relationInputKind } from './relationInputKind'
import { getFieldType } from './fieldTypes/registry'
import { isEmpty } from './fieldTypes/types'
import { isRelationLinks, visiblePayloadFields, type ResolveCollection } from './junctionLinks'

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

export type BuildItemPayloadOptions = {
  resolveCollection?: ResolveCollection
  canWriteJunction?: (rel: RelationMeta) => boolean
}

// Mixed-array write shape (chapter 12): an object element `{id, ...payload}` merges the named payload
// fields into the junction row and needs the junction collection's write grant; a bare id only
// manages membership/order. Bare ids are therefore the fallback whenever payload cannot or need not
// be sent — no grant, no visible payload fields (a sortField-only relation has no payload and the
// server rejects object elements on it), or a caller that passed no options.
function serializeLinks(rel: RelationMeta, links: RelationLink[], options: BuildItemPayloadOptions): unknown[] {
  const resolve = options.resolveCollection
  const fields = resolve ? visiblePayloadFields(rel, resolve) : []
  const writable = options.canWriteJunction?.(rel) === true
  if (!writable || fields.length === 0) return links.map((l) => l.id)
  return links.map((l) => {
    const out: Record<string, unknown> = { id: l.id }
    for (const f of fields) out[f.name] = getFieldType(f.interface).serialize(l.junction[f.name], f)
    return out
  })
}

export function buildItemPayload(
  meta: CollectionMeta,
  model: FormModel,
  locales: LanguageInfo[],
  mode: 'create' | 'update',
  options: BuildItemPayloadOptions = {},
): Record<string, unknown> {
  const { shared, translatable } = splitFields(meta)
  const payload: Record<string, unknown> = {}

  for (const f of shared) {
    // The registry's serialize owns per-type coercion — e.g. file/image '' -> null so a
    // nullable Guid FK never serialises "" (Postgres 22P02 -> 500). See fieldTypes/registry.ts.
    const v = getFieldType(f.interface).serialize(model.shared[f.name], f)
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
    for (const f of translatable) entry[f.name] = getFieldType(f.interface).serialize(values[f.name], f)
    translations[loc.code] = entry
  }
  const relations = model.relations ?? {}
  for (const rel of meta.relations ?? []) {
    if (!(rel.name in relations)) continue // untouched -> partial update
    const kind = relationInputKind(rel.interface)
    if (kind === 'dropdown' || kind === 'treeSelect') {
      if (rel.foreignKey) payload[camel(rel.foreignKey)] = relations[rel.name] ?? null
    } else if (kind === 'tagSelect') {
      const v = relations[rel.name]
      payload[rel.name] = isRelationLinks(v) ? serializeLinks(rel, v, options) : (v ?? [])
    }
    // relatedList / readonly: never written
  }

  if (Object.keys(translations).length > 0) payload.translations = translations

  // Echo the concurrency token on update so the server can reject a stale write with 409.
  if (mode === 'update' && typeof model.version === 'number') payload.version = model.version

  return payload
}
