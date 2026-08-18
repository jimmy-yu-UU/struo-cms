import { describe, it, expect } from 'vitest'
import zhTW from './zh-TW'
import en from './en'

function keyPaths(obj: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(obj).flatMap(([k, v]) =>
    v !== null && typeof v === 'object'
      ? keyPaths(v as Record<string, unknown>, `${prefix}${k}.`)
      : [`${prefix}${k}`],
  )
}

describe('locale packs', () => {
  it('zh-TW and en expose symmetric key sets', () => {
    expect(keyPaths(zhTW).sort()).toEqual(keyPaths(en).sort())
  })

  it('carry the seeded common namespace', () => {
    expect(zhTW.common.logout).toBe('登出')
    expect(en.common.logout).toBe('Log out')
  })

  it('carries the itemForm namespace in both packs', () => {
    expect(zhTW.itemForm.save).toBe('儲存')
    expect(en.itemForm.save).toBe('Save')
  })

  it('carries the revisions namespace in both packs', () => {
    expect(zhTW.revisions.open).toBe('歷史紀錄')
    expect(en.revisions.open).toBe('History')
  })

  // Pins the maintainer's 2026-08-12 ruling against the real shipped pack, not a suite-local
  // mirror: every component test that asserts an accessible name built from these separators
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
