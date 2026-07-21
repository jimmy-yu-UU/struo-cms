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
})
