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

  it('names the API access token in the password section description in both locales', () => {
    expect(zhTW.password.sectionDescription).toBe('為此帳號設定新密碼；它的工作階段與 API 存取權杖會被撤銷。')
    expect(catalogs.en.password.sectionDescription).toBe('Set a new password for this account. Its sessions and API access token are revoked.')
  })

  // The zh-TW password-change toasts name the API access token; the wording is a maintainer ruling.
  it('pins the zh-TW password-change toast wording to the ruled sentences', () => {
    expect(zhTW.password.changed).toBe('密碼已更新，API 存取權杖已撤銷。')
    expect(zhTW.password.changedSelf).toBe('密碼已更新，你的 API 存取權杖已撤銷。')
  })

  // The one name of the credential is 「API 存取權杖」 / "API access token", a maintainer ruling. Every string
  // that names it, in the password section and in the access-token panel, carries the full name.
  it('names the credential in full wherever a string names it', () => {
    const keys = [
      'password.sectionDescription', 'password.tokenRevokeNote', 'password.tokenRevokeNoteSelf',
      'password.changed', 'password.changedSelf',
      'accessToken.title', 'accessToken.none', 'accessToken.regenerateConfirmHeader',
      'accessToken.revokeConfirmHeader', 'accessToken.issuedTitle', 'accessToken.revoked',
      'accessToken.generateFailed', 'accessToken.revokeFailed', 'accessToken.loadFailed',
    ]
    const full: Record<string, RegExp> = { 'zh-TW': /API 存取權杖/, en: /API access token/i }
    for (const locale of CATALOG_LOCALES) {
      const leaves = new Map(stringLeaves(catalogs[locale] as unknown as Record<string, unknown>))
      for (const key of keys) expect(leaves.get(key), `${locale} ${key}`).toMatch(full[locale])
    }
  })

  // A half-width , ; : ? ! after a Chinese character, a closing Chinese bracket or quote, or the closing
  // brace of a placeholder, in a string that holds Chinese, belongs to the full-width forms. Half-width
  // parentheses are out of scope. No string is exempt.
  it('writes zh-TW punctuation after Chinese text in full width', () => {
    const offenders = stringLeaves(zhTW as unknown as Record<string, unknown>)
      .filter(([, value]) => /[㐀-鿿]/.test(value) && /[㐀-鿿」』）】》}]\s*[,;:?!]/.test(value))
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
