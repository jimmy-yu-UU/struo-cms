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
 * The sanctioned conversion from an untyped `/items/file` API row into a `FileRow` — do not cast
 * directly (`row as unknown as FileRow`) at a call site instead. Coerces each field defensively
 * so a malformed/missing value degrades to a safe default rather than propagating `unknown`
 * through the UI unchecked, which a direct cast would skip entirely.
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
