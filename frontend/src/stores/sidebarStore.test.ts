import { describe, it, expect, beforeEach } from 'vitest'
import { setActivePinia, createPinia } from 'pinia'
import { useSidebarStore } from './sidebarStore'

describe('sidebarStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
  })

  it('defaults to expanded when nothing is persisted', () => {
    const s = useSidebarStore()
    expect(s.collapsed).toBe(false)
    expect(s.drawerOpen).toBe(false)
  })

  it('initialises collapsed from a persisted "collapsed" value', () => {
    localStorage.setItem('struo.sidebar', 'collapsed')
    const s = useSidebarStore()
    expect(s.collapsed).toBe(true)
  })

  it('an unknown persisted value falls back to expanded', () => {
    localStorage.setItem('struo.sidebar', 'nonsense')
    const s = useSidebarStore()
    expect(s.collapsed).toBe(false)
  })

  it('toggleCollapse flips and persists', () => {
    const s = useSidebarStore()
    s.toggleCollapse()
    expect(s.collapsed).toBe(true)
    expect(localStorage.getItem('struo.sidebar')).toBe('collapsed')
    s.toggleCollapse()
    expect(s.collapsed).toBe(false)
    expect(localStorage.getItem('struo.sidebar')).toBe('expanded')
  })

  it('drawer open/close/toggle work and are not persisted', () => {
    const s = useSidebarStore()
    s.openDrawer()
    expect(s.drawerOpen).toBe(true)
    s.closeDrawer()
    expect(s.drawerOpen).toBe(false)
    s.toggleDrawer()
    expect(s.drawerOpen).toBe(true)
    expect(localStorage.getItem('struo.sidebar')).toBeNull()
  })

  it('expand() un-collapses and persists', () => {
    const s = useSidebarStore()
    s.toggleCollapse() // -> collapsed
    s.expand()
    expect(s.collapsed).toBe(false)
    expect(localStorage.getItem('struo.sidebar')).toBe('expanded')
  })

  it('expand() is a no-op when already expanded', () => {
    const s = useSidebarStore()
    localStorage.setItem('struo.sidebar', 'sentinel')
    s.expand()
    expect(s.collapsed).toBe(false)
    expect(localStorage.getItem('struo.sidebar')).toBe('sentinel') // untouched
  })
})
