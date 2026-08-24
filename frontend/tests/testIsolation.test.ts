import { describe, it, expect, vi, afterEach } from 'vitest'
import { enableAutoUnmount, mount } from '@vue/test-utils'
import { defineComponent, onUnmounted } from 'vue'

// This file pins the GLOBAL test-isolation configuration itself -- vite.config.ts's restoreMocks /
// clearMocks and vitest.setup.ts's enableAutoUnmount -- rather than any component's behaviour.
// Turning restoreMocks/clearMocks off silently reintroduces the cross-test leakage class this
// repository already paid for once in PR #35/#36, and the symptom then is an unrelated test
// failing somewhere else entirely. It fails here instead.
//
// The auto-unmount switch has a second failure mode beyond being deleted: it can be mis-wired to
// the wrong hook (e.g. `enableAutoUnmount(afterAll)` instead of `afterEach`). @vue/test-utils'
// re-call guard below only proves *some* hook was registered, not *which* one -- a mis-wiring would
// still pass that check while wrappers survive to the end of the file. The second pair of tests
// below (the unmount-flag pair) is what actually pins "afterEach", behaviourally.
//
// Two PAIRS of assertions below are DELIBERATELY order-dependent: they check that state established
// in one test is gone (or changed) by the next, which is the very thing being tested. Do not "fix"
// them by making each test self-contained -- that would delete the check -- and do not insert
// another test between either pair: an inserted test would itself become the thing the second test
// observes being cleaned up, and the pair would pass while checking nothing.

const target = { call: (): string => 'real' }
const counter = vi.fn()

let unmountedFlag = false

const FlagOnUnmount = defineComponent({
  name: 'FlagOnUnmount',
  setup() {
    onUnmounted(() => {
      unmountedFlag = true
    })
    return () => null
  },
})

describe('global test isolation', () => {
  it('installs auto-unmount globally', () => {
    // @vue/test-utils throws when enableAutoUnmount is called a second time within one module
    // registry, and vitest.setup.ts already called it for this file. A second call must therefore
    // throw; if it does not, the global hook is missing.
    expect(() => enableAutoUnmount(afterEach))
      .toThrow('enableAutoUnmount cannot be called more than once')
  })

  it('mounts a component whose unmount has not happened yet', () => {
    mount(FlagOnUnmount)
    // Proves the flag flip below is not a synchronous artefact of mount() itself -- the wrapper
    // is still alive here, and only the global afterEach hook (asserted by the next test) tears
    // it down.
    expect(unmountedFlag).toBe(false)
  })

  it('has unmounted the previous test\'s wrapper by the time this test runs', () => {
    // Depends on the test above having run first (see the file header). This can only be true if
    // something unmounted that wrapper between the two tests -- i.e. a hook wired to `afterEach`.
    // Wired to `afterAll` instead, the wrapper would still be mounted here and this would fail.
    expect(unmountedFlag).toBe(true)
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
