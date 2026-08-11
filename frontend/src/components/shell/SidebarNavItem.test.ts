import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { SidebarProvider } from '@/components/ui/sidebar'
import { h } from 'vue'
import SidebarNavItem from './SidebarNavItem.vue'

// SidebarMenuButton reads the provider context, so every mount needs a provider wrapper.
// h()'s overload resolution needs a props shape it can distinguish from VNode children —
// SidebarNavItem's own prop types (not a bare Record<string, unknown>) give it that.
type ItemProps = { label: string; icon?: string | null; active?: boolean; sub?: boolean }
function mountItem(props: ItemProps) {
  return mount(SidebarProvider, { slots: { default: () => h(SidebarNavItem, props) } })
}

describe('SidebarNavItem', () => {
  it('renders the label inside a button', () => {
    const w = mountItem({ label: 'Articles' })
    expect(w.get('button').text()).toContain('Articles')
  })

  it('emits activate on click', async () => {
    const w = mountItem({ label: 'Articles' })
    await w.get('button').trigger('click')
    expect(w.findComponent(SidebarNavItem).emitted('activate')).toHaveLength(1)
  })

  it('marks the active item with data-active', () => {
    expect(mountItem({ label: 'A', active: true }).get('button').attributes('data-active')).toBe('true')
    expect(mountItem({ label: 'A', active: false }).get('button').attributes('data-active')).not.toBe('true')
  })

  // The backend emits semantic icon names ("article"), never CSS classes. The old
  // implementation bound them as a class, so they rendered nothing.
  it('renders an svg icon for a semantic backend icon name', () => {
    expect(mountItem({ label: 'A', icon: 'article' }).find('svg').exists()).toBe(true)
  })

  it('renders a fallback svg icon when no icon is given', () => {
    expect(mountItem({ label: 'A' }).find('svg').exists()).toBe(true)
  })

  // brand-spec §5 gives sub-items their own geometry (h-7 / px-2 + guide rail); that lives
  // in SidebarMenuSubButton, not SidebarMenuButton.
  it('renders a sub-level button when sub is set', () => {
    const top = mountItem({ label: 'A' })
    const sub = mountItem({ label: 'A', sub: true })
    expect(top.get('button').attributes('data-sidebar')).toBe('menu-button')
    expect(sub.get('button').attributes('data-sidebar')).toBe('menu-sub-button')
  })

  // SidebarMenuSubButton defaults `as` to "a" with no href — reka's Primitive renders
  // that as a plain, keyboard-unreachable, non-interactive anchor. A real <button> is
  // required for both variants; `get('button')` above already encodes this, but assert
  // it explicitly so a regression back to the anchor fails loudly and by name.
  it('never renders a sub-level item as a bare anchor', () => {
    const sub = mountItem({ label: 'A', sub: true })
    expect(sub.find('a').exists()).toBe(false)
  })

  // SidebarMenuSubButton, unlike the top-level SidebarMenuButton, carries no `w-full` of its
  // own — without it the sub-button shrinks to its label's content width and only the text (not
  // the full row) responds to a click. Without SidebarNavItem passing `class="w-full"` through,
  // this assertion fails.
  it('passes w-full through to the sub-level button so the whole row is clickable', () => {
    const sub = mountItem({ label: 'A', sub: true })
    expect(sub.get('button').classes()).toContain('w-full')
  })
})
