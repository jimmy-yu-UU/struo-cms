import { afterEach, describe, expect, it, vi } from 'vitest'
import { generatePassword, PASSWORD_CHARSET } from './passwordGenerator'

describe('generatePassword', () => {
  // Spies on the global CSPRNG are installed in some of these tests. Restoring unconditionally here
  // (rather than inline after each test's assertions) means a failed assertion can never leave a
  // spy — including the biased mockImplementation below — installed for the rest of the run.
  afterEach(() => {
    vi.restoreAllMocks()
  })

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
  })

  it('only emits characters from the declared charset', () => {
    for (const ch of generatePassword(64)) expect(PASSWORD_CHARSET).toContain(ch)
  })

  // The generated password's whole purpose is being read aloud or pasted to a colleague.
  it('excludes look-alike characters', () => {
    for (const ch of 'O0lI1') expect(PASSWORD_CHARSET).not.toContain(ch)
  })

  // Locks the two inputs the rejection-sampling arithmetic (and the test below) depends on: if a
  // future edit to the charset duplicates a character or changes its length, this fails before the
  // arithmetic silently goes wrong.
  it('has a 68-character charset with no duplicate characters', () => {
    expect(PASSWORD_CHARSET).toHaveLength(68)
    expect(new Set(PASSWORD_CHARSET).size).toBe(PASSWORD_CHARSET.length)
  })

  // Pins the one property none of the tests above actually exercise: a `%`-folding implementation
  // (delete the `byte >= MAX_ACCEPTABLE_BYTE` guard, or hardcode the threshold to 256) would pass
  // every test above — lengths, charset-only output, CSPRNG-as-source, excluded look-alikes — while
  // still being biased. This test fails against that implementation.
  it('rejects out-of-range bytes instead of folding them', () => {
    // MAX_ACCEPTABLE_BYTE = floor(256 / 68) * 68 = 204, so 250 (>= 204) must be discarded and
    // redrawn rather than folded. A `%`-folding implementation would emit
    // PASSWORD_CHARSET[250 % 68] === PASSWORD_CHARSET[46] from the first draw and never redraw;
    // this implementation must discard it and take a second `getRandomValues` call of all-zero
    // bytes, producing PASSWORD_CHARSET[0] repeated.
    let call = 0
    const spy = vi.spyOn(globalThis.crypto, 'getRandomValues')
    spy.mockImplementation(((array: Uint8Array) => {
      array.fill(call === 0 ? 250 : 0)
      call += 1
      return array
    }) as typeof globalThis.crypto.getRandomValues)

    expect(generatePassword(20)).toBe(PASSWORD_CHARSET[0].repeat(20))
    expect(spy).toHaveBeenCalledTimes(2)
  })
})
