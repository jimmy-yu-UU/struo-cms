import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import SidebarNavItem from './SidebarNavItem.vue'

describe('SidebarNavItem', () => {
  it('renders label + fallback icon and emits activate on click', async () => {
    const wrapper = mount(SidebarNavItem, { props: { label: 'Article' } })
    expect(wrapper.text()).toContain('Article')
    expect(wrapper.find('.pi-file').exists()).toBe(true)
    await wrapper.find('button.nav-item').trigger('click')
    expect(wrapper.emitted('activate')).toHaveLength(1)
  })

  it('uses the provided icon and marks active', () => {
    const wrapper = mount(SidebarNavItem, {
      props: { label: 'Dashboard', icon: 'pi pi-th-large', active: true },
    })
    expect(wrapper.find('.pi-th-large').exists()).toBe(true)
    expect(wrapper.find('button.nav-item').classes()).toContain('active')
  })
})
