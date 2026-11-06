import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import PasswordPanel from './PasswordPanel.vue'

const i18n = createI18n({
  legacy: false, locale: 'en',
  messages: { en: { password: {
    sectionTitle: 'Password', sectionDescription: 'Set a new password for this account.', resetTitle: 'Reset password',
  } } },
})

function mountPanel(props: { disabled?: boolean; disabledHint?: string } = {}) {
  return mount(PasswordPanel, { props, global: { plugins: [i18n] } })
}

describe('PasswordPanel', () => {
  it('has a heading, a one-line description and one auto-width button', () => {
    const w = mountPanel()

    expect(w.get('h2').text()).toBe('Password')
    expect(w.get('p').text()).toBe('Set a new password for this account.')
    const buttons = w.findAll('button')
    expect(buttons).toHaveLength(1)
    expect(buttons[0].text()).toBe('Reset password')
    expect(buttons[0].attributes('type')).toBe('button')
    expect(buttons[0].classes()).not.toContain('w-full')
  })

  it('raises reset when the button is pressed', async () => {
    const w = mountPanel()

    await w.get('button').trigger('click')

    expect(w.emitted('reset')).toHaveLength(1)
  })

  it('disables the button and carries the reason as a tooltip on the button and its span', () => {
    const w = mountPanel({ disabled: true, disabledHint: 'Only a super-admin can change it.' })

    const button = w.get('button')
    expect(button.attributes('disabled')).toBeDefined()
    expect(button.attributes('title')).toBe('Only a super-admin can change it.')
    expect(button.element.parentElement!.getAttribute('title')).toBe('Only a super-admin can change it.')
  })

  it('has no tooltip when it is enabled', () => {
    const w = mountPanel({ disabledHint: 'ignored' })

    expect(w.get('button').attributes('title')).toBeUndefined()
  })
})
