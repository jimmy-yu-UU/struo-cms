import { describe, it, expect, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import BrandMark from './BrandMark.vue'
import { useAppConfigStore } from '../../stores/appConfigStore'

describe('BrandMark', () => {
  beforeEach(() => setActivePinia(createPinia()))

  it('renders the letter mark when no logo is configured', () => {
    const store = useAppConfigStore()
    store.brandName = 'Acme'
    store.brandLogoUrl = null
    const wrapper = mount(BrandMark)
    expect(wrapper.find('img.brand-logo').exists()).toBe(false)
    expect(wrapper.find('span.mark').text()).toBe('A')
  })

  it('renders the logo image when a logo URL is configured', () => {
    const store = useAppConfigStore()
    store.brandName = 'Acme'
    store.brandLogoUrl = 'https://cdn/logo.svg'
    const wrapper = mount(BrandMark)
    const img = wrapper.find('img.brand-logo')
    expect(img.exists()).toBe(true)
    expect(img.attributes('src')).toBe('https://cdn/logo.svg')
    expect(img.attributes('alt')).toBe('Acme')
    expect(wrapper.find('span.mark').exists()).toBe(false)
  })
})
