import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import PasswordInput from './PasswordInput.vue'
import en from '@/locales/en'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
const opts = { global: { plugins: [i18n] } }

describe('PasswordInput', () => {
  it('masks the value by default', () => {
    const w = mount(PasswordInput, { props: { modelValue: 'secret' }, ...opts })
    expect(w.get('input').attributes('type')).toBe('password')
  })

  it('reveals and re-masks on the toggle button', async () => {
    const w = mount(PasswordInput, { props: { modelValue: 'secret' }, ...opts })
    await w.get('button').trigger('click')
    expect(w.get('input').attributes('type')).toBe('text')
    await w.get('button').trigger('click')
    expect(w.get('input').attributes('type')).toBe('password')
  })

  // The toggle is icon-only, so its accessible name is the only thing a screen-reader user gets —
  // and it must change with the state, otherwise "Show password" lies once revealed.
  it('names the toggle for its next action', async () => {
    const w = mount(PasswordInput, { props: { modelValue: 'secret' }, ...opts })
    expect(w.get('button').attributes('aria-label')).toBe(en.login.showPassword)
    await w.get('button').trigger('click')
    expect(w.get('button').attributes('aria-label')).toBe(en.login.hidePassword)
  })

  // Native <button> defaults to type="submit". The intended consumer is a login <form>, so an
  // untyped toggle would submit that form instead of revealing the password.
  it('gives the toggle a non-submitting type', () => {
    const w = mount(PasswordInput, { props: { modelValue: 'secret' }, ...opts })
    expect(w.get('button').attributes('type')).toBe('button')
  })

  it('relays typing', async () => {
    const w = mount(PasswordInput, { props: { modelValue: '' }, ...opts })
    await w.get('input').setValue('abc')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['abc'])
  })

  it('forwards id and autocomplete to the native input', () => {
    const w = mount(PasswordInput, { props: { modelValue: '', id: 'pw', autocomplete: 'current-password' }, ...opts })
    expect(w.get('input').attributes('id')).toBe('pw')
    expect(w.get('input').attributes('autocomplete')).toBe('current-password')
  })

  it('disables both the input and the toggle', () => {
    const w = mount(PasswordInput, { props: { modelValue: 'x', disabled: true }, ...opts })
    expect(w.get('input').attributes('disabled')).toBeDefined()
    expect(w.get('button').attributes('disabled')).toBeDefined()
  })

  // An assertion made only against the initial render cannot distinguish a genuinely prop-driven
  // control from one that merely seeded its local state once and stopped listening.
  it('reflects the inbound model value and follows an update made after mount', async () => {
    const w = mount(PasswordInput, { props: { modelValue: 'secret' }, ...opts })
    expect((w.get('input').element as HTMLInputElement).value).toBe('secret')
    await w.setProps({ modelValue: 'changed' })
    expect((w.get('input').element as HTMLInputElement).value).toBe('changed')
  })
})
