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

  // The zh-TW password-change toasts name the API access token; the wording is a maintainer ruling.
  it('pins the zh-TW password-change toast wording to the ruled sentences', () => {
    expect(zhTW.password.changed).toBe('密碼已更新，API 存取權杖已撤銷。')
    expect(zhTW.password.changedSelf).toBe('密碼已更新，你的 API 存取權杖已撤銷。')
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
