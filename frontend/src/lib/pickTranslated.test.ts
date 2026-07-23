import { describe, it, expect } from 'vitest'
import { pickTranslated } from './pickTranslated'

describe('pickTranslated', () => {
  const tr = { 'zh-TW': { title: '' }, en: { title: 'Hello' } }

  it('prefers the default locale when it has a value', () => {
    expect(pickTranslated({ 'zh-TW': { title: '嗨' }, en: { title: 'Hello' } }, 'zh-TW', 'title')).toBe('嗨')
  })

  it('falls back to the first locale with a non-empty value', () => {
    expect(pickTranslated(tr, 'zh-TW', 'title')).toBe('Hello')
  })

  it('returns undefined when no locale has a value', () => {
    expect(pickTranslated({ en: { title: '' } }, 'zh-TW', 'title')).toBeUndefined()
    expect(pickTranslated(undefined, 'zh-TW', 'title')).toBeUndefined()
  })
})
