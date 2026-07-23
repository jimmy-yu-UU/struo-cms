import type { CollectionMeta } from '../types/schema'

export type Crumb = { label: string; to?: { name: string; params?: Record<string, string> } }

const HOME: Crumb = { label: 'nav.dashboard', to: { name: 'dashboard' } }

export function buildBreadcrumb(
  route: { name?: string | null; params?: Record<string, string> },
  collections: CollectionMeta[],
  t: (key: string) => string,
): Crumb[] {
  const name = route.name ?? ''
  const params = route.params ?? {}

  if (name === 'dashboard') return [{ label: t('nav.dashboard') }]
  if (name === 'media') return [{ ...HOME, label: t('nav.dashboard') }, { label: t('nav.media') }]
  if (name === 'settings')
    return [{ ...HOME, label: t('nav.dashboard') }, { label: t('nav.settings') }]

  const collectionName = params.name ?? ''
  const meta = collections.find((c) => c.name === collectionName)
  const crumbs: Crumb[] = [{ ...HOME, label: t('nav.dashboard') }]

  if (meta?.group && meta.group.trim() !== '') crumbs.push({ label: meta.group })

  const collectionLabel = meta?.label ?? collectionName
  // Unknown route with no collection param: never emit an empty-label crumb.
  if (collectionLabel === '') return crumbs

  const isLeaf = name === 'collection-create' || name === 'collection-item'
  crumbs.push(
    isLeaf
      ? { label: collectionLabel, to: { name: 'collection-list', params: { name: collectionName } } }
      : { label: collectionLabel },
  )

  if (name === 'collection-create') crumbs.push({ label: t('breadcrumb.newItem') })
  if (name === 'collection-item') crumbs.push({ label: t('breadcrumb.editItem') })

  return crumbs
}
