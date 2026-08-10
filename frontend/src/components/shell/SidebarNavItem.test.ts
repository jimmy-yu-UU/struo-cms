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
    expect(sub.get('a, button').attributes('data-sidebar')).toBe('menu-sub-button')
  })
})
