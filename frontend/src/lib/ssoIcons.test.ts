import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { KeyRound } from '@lucide/vue'
import { resolveSsoIcon } from './ssoIcons'

describe('resolveSsoIcon', () => {
  it('renders the four-square Microsoft mark', () => {
    const w = mount(resolveSsoIcon('microsoft'))
    expect(w.findAll('rect')).toHaveLength(4)
  })

  it('renders the multicolour Google mark', () => {
    const w = mount(resolveSsoIcon('google'))
    expect(w.findAll('path')).toHaveLength(4)
  })

  it('is case-insensitive', () => {
    expect(resolveSsoIcon('Google')).toBe(resolveSsoIcon('google'))
  })

  it.each([[''], ['nope'], ['constructor'], ['__proto__']])('falls back to the key icon for %j', (name) => {
    expect(resolveSsoIcon(name)).toBe(KeyRound)
  })

  it('falls back to the key icon for a missing name', () => {
    expect(resolveSsoIcon(undefined)).toBe(KeyRound)
    expect(resolveSsoIcon(null)).toBe(KeyRound)
  })
})
