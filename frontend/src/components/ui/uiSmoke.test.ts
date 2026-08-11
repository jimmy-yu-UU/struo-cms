import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'

// A vendored component that cannot mount means the install is broken (missing peer,
// unresolved @/lib/utils, or a jsdom gap). Catch that here, not inside a real view.
describe('vendored shadcn-vue atoms', () => {
  it('mounts Button and renders its slot', () => {
    const w = mount(Button, { slots: { default: 'Save' } })
    expect(w.text()).toBe('Save')
    expect(w.element.tagName).toBe('BUTTON')
  })

  it('applies the destructive variant without dropping caller classes', () => {
    const w = mount(Button, { props: { variant: 'destructive', class: 'w-full' } })
    expect(w.attributes('class')).toContain('w-full')
  })

  it('mounts Badge and Input', () => {
    expect(mount(Badge, { slots: { default: 'Draft' } }).text()).toBe('Draft')
    expect(mount(Input).element.tagName).toBe('INPUT')
  })
})
