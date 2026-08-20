import { describe, expect, it, vi } from 'vitest'
import { generatePassword, PASSWORD_CHARSET } from './passwordGenerator'

describe('generatePassword', () => {
  it('never falls below 20 characters', () => {
    expect(generatePassword(8)).toHaveLength(20)
  })

  it('honours a minimum above 20', () => {
    expect(generatePassword(32)).toHaveLength(32)
  })

  // Security-relevant and easy for a later refactor to quietly break: this must be a CSPRNG.
  it('draws from crypto.getRandomValues, never Math.random', () => {
    const cryptoSpy = vi.spyOn(globalThis.crypto, 'getRandomValues')
    const mathSpy = vi.spyOn(Math, 'random')

    generatePassword(8)

    expect(cryptoSpy).toHaveBeenCalled()
    expect(mathSpy).not.toHaveBeenCalled()
    cryptoSpy.mockRestore()
    mathSpy.mockRestore()
  })

  it('only emits characters from the declared charset', () => {
    for (const ch of generatePassword(64)) expect(PASSWORD_CHARSET).toContain(ch)
  })

  // The generated password's whole purpose is being read aloud or pasted to a colleague.
  it('excludes look-alike characters', () => {
    for (const ch of 'O0lI1') expect(PASSWORD_CHARSET).not.toContain(ch)
  })
})
