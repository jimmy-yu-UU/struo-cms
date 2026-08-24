import { describe, it, expect, vi, afterEach } from 'vitest'
import { enableAutoUnmount } from '@vue/test-utils'

// This file pins the GLOBAL test-isolation configuration itself -- vite.config.ts's restoreMocks /
// clearMocks and vitest.setup.ts's enableAutoUnmount -- rather than any component's behaviour.
// Turning any of the three off silently reintroduces the cross-test leakage class this repository
// already paid for once in PR #35/#36, and the symptom then is an unrelated test failing somewhere
// else entirely. It fails here instead.
//
// Two of the assertions below are DELIBERATELY order-dependent: they check that state established
// in one test is gone by the next, which is the very thing being tested. Do not "fix" them by
// making each test self-contained -- that would delete the check.

const target = { call: (): string => 'real' }
const counter = vi.fn()

describe('global test isolation', () => {
  it('installs auto-unmount globally', () => {
    // @vue/test-utils throws when enableAutoUnmount is called a second time within one module
    // registry, and vitest.setup.ts already called it for this file. A second call must therefore
    // throw; if it does not, the global hook is missing.
    expect(() => enableAutoUnmount(afterEach))
      .toThrow('enableAutoUnmount cannot be called more than once')
  })

  it('installs a spy and a mock call for the next test to check', () => {
    vi.spyOn(target, 'call').mockReturnValue('spied')
    counter()
    expect(target.call()).toBe('spied')
    expect(counter).toHaveBeenCalledTimes(1)
  })

  it('restores spies and clears mock history before this test runs', () => {
    // Depends on the test above having run first (see the file header).
    expect(target.call()).toBe('real')        // restoreMocks
    expect(counter).toHaveBeenCalledTimes(0)  // clearMocks
  })
})
