// Accepts only a same-origin path: it starts with "/" but not "//" (protocol-relative) or "/\"
// (which browsers read as "//"). Mirrors the server's LocalRedirect.Sanitize, which re-checks the
// OIDC returnUrl; this copy keeps the password-login redirect and the SSO hand-off in step with it.
const BACKSLASH = String.fromCharCode(92)

export function safeRedirect(value: unknown): string | null {
  if (typeof value !== 'string' || value === '') return null
  if (!value.startsWith('/') || value.startsWith('//') || value.startsWith('/' + BACKSLASH)) return null
  return value
}

// Query-string encoding for the SSO returnUrl. Slashes stay readable, as they are legal in a query.
export function encodeRedirectParam(path: string): string {
  return encodeURIComponent(path).replaceAll('%2F', '/')
}
