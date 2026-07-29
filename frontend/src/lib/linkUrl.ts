// Defense-in-depth allowlist for user-supplied link URLs.
//
// The TipTap Link extension is already configured with protocols
// ['http','https','mailto'] and the server sanitizes stored HTML, so this is a
// belt-and-suspenders guard on the toolbar prompt path only. It rejects
// dangerous schemes (javascript:, data:, vbscript:, …) before they ever reach
// the editor command.
const ALLOWED = /^(https?:|mailto:)/i

export function isAllowedLinkUrl(url: string): boolean {
  const trimmed = url.trim()
  if (trimmed === '') return false
  return ALLOWED.test(trimmed)
}
