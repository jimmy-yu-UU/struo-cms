import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatCard from './StatCard.vue'

describe('StatCard', () => {
  it('renders caption and value', () => {
    const w = mount(StatCard, { props: { caption: 'Content items', value: 128 } })
    expect(w.text()).toContain('Content items')
    expect(w.text()).toContain('128')
  })
})
