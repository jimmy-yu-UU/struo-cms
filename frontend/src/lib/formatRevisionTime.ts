import { formatDateTime } from './formatDateTime'

/** Invalid input falls back to the raw string so revision rows never render "Invalid Date". */
export function formatRevisionTime(iso: string): string {
  const s = formatDateTime(iso)
  return s === '—' ? (iso || '—') : s
}
