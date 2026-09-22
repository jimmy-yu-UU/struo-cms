// Renders an unknown value for display without ever falling back to the default
// `Object.prototype.toString` result ("[object Object]"). Primitives render in their natural
// string form; anything else (object, array, function, symbol) renders as JSON, or as an empty
// string for the rare value JSON.stringify itself returns undefined for (a function or symbol).
export function toDisplayString(v: unknown): string {
  if (v === null || v === undefined) return ''
  if (typeof v === 'string') return v
  if (typeof v === 'number' || typeof v === 'boolean' || typeof v === 'bigint') return String(v)
  return JSON.stringify(v) ?? ''
}
