import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { debounce } from './debounce'

describe('debounce', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('collapses rapid calls into a single trailing invocation', () => {
    const fn = vi.fn()
    const d = debounce(fn, 300)
    d()
    d()
    d()
    expect(fn).not.toHaveBeenCalled()
    vi.advanceTimersByTime(300)
    expect(fn).toHaveBeenCalledTimes(1)
  })

  it('passes the arguments of the last call through', () => {
    const fn = vi.fn()
    const d = debounce(fn, 300)
    d('a')
    d('b')
    vi.advanceTimersByTime(300)
    expect(fn).toHaveBeenCalledWith('b')
  })

  it('fires again after the wait window elapses between call bursts', () => {
    const fn = vi.fn()
    const d = debounce(fn, 300)
    d()
    vi.advanceTimersByTime(300)
    d()
    vi.advanceTimersByTime(300)
    expect(fn).toHaveBeenCalledTimes(2)
  })

  it('cancel() prevents a pending invocation', () => {
    const fn = vi.fn()
    const d = debounce(fn, 300)
    d()
    d.cancel()
    vi.advanceTimersByTime(300)
    expect(fn).not.toHaveBeenCalled()
  })
})
