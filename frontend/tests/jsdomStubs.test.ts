import { describe, it, expect } from 'vitest'

// reka-ui's floating components and SidebarProvider touch these on mount. jsdom ships
// none of them; without stubs the failure surfaces as an unrelated component crash.
describe('jsdom environment stubs', () => {
  it('provides matchMedia returning a usable MediaQueryList', () => {
    const mql = window.matchMedia('(max-width: 768px)')
    expect(mql.matches).toBe(false)
    expect(mql.media).toBe('(max-width: 768px)')
    expect(() => mql.addEventListener('change', () => {})).not.toThrow()
    expect(() => mql.removeEventListener('change', () => {})).not.toThrow()
  })

  it('provides observer constructors', () => {
    expect(() => new ResizeObserver(() => {}).observe(document.body)).not.toThrow()
    expect(() => new IntersectionObserver(() => {}).observe(document.body)).not.toThrow()
  })

  it('provides pointer-capture and scrollIntoView on Element', () => {
    const el = document.createElement('div')
    expect(el.hasPointerCapture(1)).toBe(false)
    expect(() => el.setPointerCapture(1)).not.toThrow()
    expect(() => el.releasePointerCapture(1)).not.toThrow()
    expect(() => el.scrollIntoView()).not.toThrow()
  })
})
