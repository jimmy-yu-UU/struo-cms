import type { CollectionMeta, RelationMeta } from '../types/schema'
import { readField, resolveItemTitle, type TitleRow } from './resolveItemTitle'
import { toDisplayString } from './toDisplayString'

export function resolveDisplayLabel(
  row: TitleRow,
  relation: RelationMeta,
  targetMeta: CollectionMeta,
  locale: string,
): string {
  const template = relation.displayTemplate
  if (template) {
    const out = template.replace(/\{(\w+)\}/g, (_m, token: string) => {
      const v = readField(row, targetMeta, locale, token)
      return v == null || v === '' ? '' : toDisplayString(v)
    })
    if (out.trim() !== '') return out
  }
  return resolveItemTitle(row, targetMeta, locale)
}
