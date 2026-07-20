import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import PageHeader from './PageHeader.vue'

describe('PageHeader', () => {
  it('renders the title in an h1', () => {
    const w = mount(PageHeader, { props: { title: 'Articles' } })
    expect(w.get('h1').text()).toBe('Articles')
  })
  it('renders the caption when provided', () => {
    const w = mount(PageHeader, { props: { title: 'Articles', caption: '128 items' } })
    expect(w.get('.caption').text()).toBe('128 items')
  })
  it('omits the caption element when caption is absent', () => {
    const w = mount(PageHeader, { props: { title: 'Articles' } })
    expect(w.find('.caption').exists()).toBe(false)
  })
  it('renders the actions slot', () => {
    const w = mount(PageHeader, { props: { title: 'Articles' }, slots: { actions: '<button>New</button>' } })
    expect(w.get('.head-actions').text()).toBe('New')
  })
})
