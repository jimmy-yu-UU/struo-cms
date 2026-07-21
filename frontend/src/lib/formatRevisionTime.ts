// Formats a revision's ISO timestamp for display. Guards against an absent/invalid value
// (which `new Date(bad).toLocaleString()` would render as "Invalid Date").
export function formatRevisionTime(iso: string): string {
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? (iso || '—') : d.toLocaleString()
}
