// Renders an unknown value for display without ever falling back to the default
// `Object.prototype.toString` result ("[object Object]"). null and undefined render as an empty
// string; a string, number, boolean or bigint renders as itself; anything else (object, array,
// function, symbol) renders as JSON, or as an empty string where JSON.stringify returns undefined
// (a function or symbol).
export function toDisplayString(v: unknown): string {
  if (v === null || v === undefined) return ''
  if (typeof v === 'string') return v
  if (typeof v === 'number' || typeof v === 'boolean' || typeof v === 'bigint') return String(v)
  return JSON.stringify(v) ?? ''
}
