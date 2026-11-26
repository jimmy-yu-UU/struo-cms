import { describe, it, expect } from 'vitest'
import zhTW from './zh-TW'
import { catalogs, CATALOG_LOCALES } from './index'

function keyPaths(obj: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) =>
    v !== null && typeof v === 'object'
      ? keyPaths(v as Record<string, unknown>, `${prefix}${k}.`)
      : [`${prefix}${k}`],
  )
}

function stringLeaves(obj: Record<string, unknown>, prefix = ''): Array<[string, string]> {
  return Object.entries(obj).flatMap(([k, v]): Array<[string, string]> => {
    if (typeof v === 'string') return [[`${prefix}${k}`, v]]
    return v !== null && typeof v === 'object' ? stringLeaves(v as Record<string, unknown>, `${prefix}${k}.`) : []
  })
}

describe('locale packs', () => {
  it('every bundled catalog exposes the same key set', () => {
    const [first, ...rest] = CATALOG_LOCALES
    const reference = keyPaths(catalogs[first] as Record<string, unknown>).sort()
    for (const locale of rest) {
      expect(keyPaths(catalogs[locale] as Record<string, unknown>).sort(), `catalog ${locale}`).toEqual(reference)
    }
  })

  it('every catalog names every catalog under lang.*', () => {
    for (const locale of CATALOG_LOCALES) {
      const lang = (catalogs[locale] as { lang: Record<string, string> }).lang
      for (const other of CATALOG_LOCALES) expect(lang[other], `${locale}.lang.${other}`).toBeTruthy()
    }
  })

  it('ships zh-TW and en', () => {
    expect([...CATALOG_LOCALES].sort()).toEqual(['en', 'zh-TW'])
  })

  it('names the access token in the password section description in both locales', () => {
    expect(zhTW.password.sectionDescription).toBe('為此帳號設定新密碼；它的工作階段與存取權杖會被撤銷。')
    expect(catalogs.en.password.sectionDescription).toBe('Set a new password for this account. Its sessions and access token are revoked.')
  })

  it('pins the zh-TW password-change toast wording to the ruled sentences', () => {
    expect(zhTW.password.changed).toBe('密碼已更新，存取權杖已撤銷。')
    expect(zhTW.password.changedSelf).toBe('密碼已更新，你的存取權杖已撤銷。')
  })

  it('calls the token the same thing in the password section as in the access-token panel', () => {
    for (const locale of CATALOG_LOCALES) {
      const { password } = catalogs[locale] as { password: Record<string, string> }
      for (const key of ['sectionDescription', 'tokenRevokeNote', 'tokenRevokeNoteSelf', 'changed', 'changedSelf']) {
        expect(password[key], `${locale} password.${key}`).not.toMatch(/\bAPI\b/)
      }
    }
  })

  // A half-width , ; : ? ! right after a Chinese character belongs to the full-width forms.
  // No string is exempt.
  it('writes zh-TW punctuation after a Chinese character in full width', () => {
    const offenders = stringLeaves(zhTW as unknown as Record<string, unknown>)
      .filter(([, value]) => /[㐀-鿿][,;:?!]/.test(value))
      .map(([path, value]) => `${path}: ${value}`)
    expect(offenders).toEqual([])
  })

  // Pins the maintainer's 2026-08-12 ruling against the real shipped pack, not a suite-local mirror: narrative-guard:allow: cites the specific ruling this test pins against, not a changelog date
  // every component test that asserts an accessible name built from these separators
  // reads them back off this same object, so a mutation here propagates to "expected" and
  // "actual" together and would stay green. nameListSeparator is the sentence comma '，' — it
  // joins a field label to a state description ("地區，已選 1 項"), two unlike phrases, not a
  // list of like items, so the enumeration comma '、' is wrong here even though it reads more
  // naturally for an actual list.
  it('pins the zh-TW separators to the maintainer-ruled characters, not ASCII or the enumeration comma', () => {
    expect(zhTW.fields.namePairSeparator).toBe('：')
    expect(zhTW.fields.nameListSeparator).toBe('，')
  })
})
