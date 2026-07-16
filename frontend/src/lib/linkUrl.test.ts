import { describe, it, expect } from 'vitest'
import { isAllowedLinkUrl } from './linkUrl'

describe('isAllowedLinkUrl', () => {
  it('allows http and https URLs', () => {
    expect(isAllowedLinkUrl('http://example.com')).toBe(true)
    expect(isAllowedLinkUrl('https://example.com/path?q=1')).toBe(true)
  })

  it('allows mailto URLs', () => {
    expect(isAllowedLinkUrl('mailto:someone@example.com')).toBe(true)
  })

  it('is case-insensitive on the scheme', () => {
    expect(isAllowedLinkUrl('HTTPS://EXAMPLE.COM')).toBe(true)
    expect(isAllowedLinkUrl('MailTo:a@b.com')).toBe(true)
  })

  it('trims surrounding whitespace before matching', () => {
    expect(isAllowedLinkUrl('  https://example.com  ')).toBe(true)
  })

  it('rejects javascript: URLs', () => {
    expect(isAllowedLinkUrl('javascript:alert(1)')).toBe(false)
    expect(isAllowedLinkUrl('  javascript:alert(1)')).toBe(false)
    expect(isAllowedLinkUrl('JavaScript:alert(1)')).toBe(false)
  })

  it('rejects other dangerous or unknown schemes', () => {
    expect(isAllowedLinkUrl('data:text/html,<script>1</script>')).toBe(false)
    expect(isAllowedLinkUrl('vbscript:msgbox(1)')).toBe(false)
    expect(isAllowedLinkUrl('ftp://example.com')).toBe(false)
    expect(isAllowedLinkUrl('/relative/path')).toBe(false)
  })

  it('rejects empty and whitespace-only strings', () => {
    expect(isAllowedLinkUrl('')).toBe(false)
    expect(isAllowedLinkUrl('   ')).toBe(false)
  })
})
