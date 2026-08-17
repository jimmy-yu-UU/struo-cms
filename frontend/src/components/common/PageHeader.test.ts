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
    const caption = w.get('.titles p')
    expect(caption.text()).toBe('128 items')
    // jsdom never applies <style scoped>, so this is the only place the caption's font-weight is
    // guarded: font-medium is what carries the 500 weight the caption needs.
    expect(caption.classes()).toEqual(expect.arrayContaining(['text-sm', 'font-medium', 'text-muted-foreground']))
  })
  it('omits the caption element when caption is absent', () => {
    const w = mount(PageHeader, { props: { title: 'Articles' } })
    expect(w.find('.titles p').exists()).toBe(false)
  })
  it('renders the actions slot', () => {
    const w = mount(PageHeader, { props: { title: 'Articles' }, slots: { actions: '<button>New</button>' } })
    expect(w.get('.head-actions').text()).toBe('New')
  })
  it('renders the lead slot inside .head-lead when provided', () => {
    const w = mount(PageHeader, { props: { title: 'Edit' }, slots: { lead: '<button>Back</button>' } })
    expect(w.find('.head-lead').exists()).toBe(true)
    expect(w.get('.head-lead').text()).toBe('Back')
  })
  it('omits the .head-lead wrapper when no lead slot is given', () => {
    const w = mount(PageHeader, { props: { title: 'Edit' } })
    expect(w.find('.head-lead').exists()).toBe(false)
  })
})
