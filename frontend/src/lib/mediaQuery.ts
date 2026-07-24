import type { FilterSpec } from './buildListQuery'

export type MediaType = 'all' | 'image' | 'video'
export type MediaSort = 'newest' | 'name'

// image/ and video/ each map to a single contentType starts-with condition (backend
// QueryOperator.StartsWith, REST token `_starts_with`; contentType is a non-hidden [CmsField]
// so QueryValidator allows it). "Documents" is intentionally omitted — application/* + text/*
// has no clean single-condition mapping and there is no NotStartsWith operator (see spec §0).
export function mediaTypeFilter(type: MediaType): FilterSpec | undefined {
  if (type === 'image') return { contentType: { op: '_starts_with', value: 'image/' } }
  if (type === 'video') return { contentType: { op: '_starts_with', value: 'video/' } }
  return undefined
}

// folderId is a declared ManyToOne relation FK, auto-allowed by QueryValidator. `_null` ignores
// FieldValue server-side; 'true' is a placeholder to satisfy FilterSpec's string shape.
export function mediaFolderFilter(folderId: string | null): FilterSpec {
  return folderId
    ? { folderId: { op: '_eq', value: folderId } }
    : { folderId: { op: '_null', value: 'true' } }
}

export function mediaSort(sort: MediaSort): string {
  return sort === 'name' ? 'fileName' : '-createdAt'
}
