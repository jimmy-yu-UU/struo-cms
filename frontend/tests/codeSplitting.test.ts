import { describe, it, expect } from 'vitest'
import router from '../src/router'
import { registry } from '../src/lib/fieldTypes/registry'
import type { FieldInterface } from '../src/lib/fieldTypes/types'

// Every routed view must be a lazy loader (a function), so its code lands in its own chunk.
// The parent record for AppShell stays eager: every authenticated page needs it.
const LAZY_ROUTE_NAMES = [
  'login', 'dashboard', 'media', 'settings', 'collection-list', 'collection-create', 'collection-item',
] as const

describe('route-level code splitting', () => {
  it.each(LAZY_ROUTE_NAMES)('route "%s" is a lazy import', (name) => {
    const record = router.getRoutes().find((r) => r.name === name)
    expect(record, `route ${name} exists`).toBeDefined()
    expect(typeof record!.components?.default).toBe('function')
  })

  it('the AppShell parent record is eager', () => {
    const parent = router.getRoutes().find((r) => r.path === '/' && r.name === undefined)
    expect(parent).toBeDefined()
    expect(typeof parent!.components?.default).toBe('object')
  })

  it('every named route is lazy', () => {
    const eager = router.getRoutes().filter((r) => r.name && typeof r.components?.default !== 'function')
    expect(eager.map((r) => r.name)).toEqual([])
  })
})

// Vue's defineAsyncComponent returns a wrapper component carrying an internal __asyncLoader.
// That is the only signal available without resolving the loader; if Vue renames it this test
// fails loudly rather than silently passing.
function isAsyncComponent(c: unknown): boolean {
  return typeof c === 'object' && c !== null && '__asyncLoader' in c
}

describe('field registry code splitting', () => {
  it('richText is the only async field component', () => {
    const asyncOnes = (Object.keys(registry) as FieldInterface[]).filter((k) => isAsyncComponent(registry[k].component))
    expect(asyncOnes).toEqual(['richText'])
  })
})
