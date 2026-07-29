import type { FileRow } from '../components/media/FileThumbnail.vue'

function str(v: unknown, fallback = ''): string {
  return typeof v === 'string' ? v : fallback
}
function num(v: unknown, fallback = 0): number {
  return typeof v === 'number' ? v : fallback
}
function optNum(v: unknown): number | null | undefined {
  if (typeof v === 'number') return v
  if (v === null) return null
  return undefined
}
function optStr(v: unknown): string | undefined {
  return typeof v === 'string' ? v : undefined
}

/**
 * Converts an untyped `/items/file` API row into a `FileRow`, replacing the unsafe
 * `as unknown as FileRow` double cast previously used at each call site. Coerces each
 * field defensively so a malformed/missing value degrades to a safe default rather than
 * propagating `unknown` through the UI unchecked.
 */
export function toFileRow(raw: Record<string, unknown>): FileRow {
  return {
    id: str(raw.id),
    fileName: str(raw.fileName),
    contentType: str(raw.contentType),
    size: num(raw.size),
    width: optNum(raw.width),
    height: optNum(raw.height),
    status: optStr(raw.status),
    createdAt: optStr(raw.createdAt),
  }
}

export function toFileRows(raw: Record<string, unknown>[]): FileRow[] {
  return raw.map(toFileRow)
}
