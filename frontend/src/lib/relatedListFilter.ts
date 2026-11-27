import type { CollectionMeta, RelationMeta } from '../types/schema'

function camel(s: string): string {
  return s.length ? s[0].toLowerCase() + s.slice(1) : s
}

/**
 * The field the list of a one-to-many relation filters its child collection on. A child key that sets
 * `Hidden = true` is refused by name, so when the child declares a many-to-one relation on the same key that
 * points back at the parent, the list filters on that relation's target id (`<relation>.id`), which the
 * reader of the parent form may name. Otherwise it filters on the key. Several candidates resolve to the first in
 * declaration order.
 */
export function relatedListFilterField(
  relation: RelationMeta,
  childMeta: CollectionMeta | undefined,
  parentCollection: string | undefined,
): string {
  const key = relation.foreignKey
  if (!key) return ''
  const back = parentCollection
    ? childMeta?.relations.find((r) =>
        r.kind === 'manyToOne'
        && r.targetCollection === parentCollection
        && r.foreignKey?.toLowerCase() === key.toLowerCase())
    : undefined
  return back ? `${back.name}.id` : camel(key)
}
