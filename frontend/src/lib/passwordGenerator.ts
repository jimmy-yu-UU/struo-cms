// Charset for admin-generated passwords. Look-alike characters (O/0, l/I/1) are excluded on purpose:
// this password exists to be read aloud or pasted to a colleague, so ambiguity costs more than the
// few bits of entropy the omissions give up.
export const PASSWORD_CHARSET =
  'ABCDEFGHJKLMNPQRSTUVWXYZ' + 'abcdefghijkmnpqrstuvwxyz' + '23456789' + '!@#$%^&*-_=+'

// A comfortable default well above any sane policy minimum.
const DEFAULT_LENGTH = 20

// Largest multiple of the charset size that fits in a byte. Bytes at or above this are rejected
// rather than folded with `%`, which would make the first (256 % n) characters more likely.
const MAX_ACCEPTABLE_BYTE = Math.floor(256 / PASSWORD_CHARSET.length) * PASSWORD_CHARSET.length

/**
 * Generates a random password of `max(DEFAULT_LENGTH, minLength)` characters using the platform
 * CSPRNG. Never uses Math.random — it is not cryptographically secure and this value guards an
 * account.
 */
export function generatePassword(minLength: number): string {
  const length = Math.max(DEFAULT_LENGTH, minLength)
  const out: string[] = []
  const buffer = new Uint8Array(length)

  while (out.length < length) {
    crypto.getRandomValues(buffer)
    for (const byte of buffer) {
      if (out.length === length) break
      if (byte >= MAX_ACCEPTABLE_BYTE) continue // rejection sampling — see MAX_ACCEPTABLE_BYTE
      out.push(PASSWORD_CHARSET[byte % PASSWORD_CHARSET.length])
    }
  }

  return out.join('')
}
