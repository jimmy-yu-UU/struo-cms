import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import PasswordInput from './PasswordInput.vue'
import en from '@/locales/en'
import zhTW from '@/locales/zh-TW'

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

  // id/autocomplete/required are not dedicated props — inheritAttrs is disabled and everything
  // fallthrough-eligible is forwarded to the native input via $attrs. required in particular is
  // what makes the browser's native validation fire on an empty password in the login form.
  it('forwards arbitrary attributes to the native input, not the wrapper', () => {
    const w = mount(PasswordInput, {
      props: { modelValue: '' },
      attrs: { id: 'pw', autocomplete: 'current-password', required: true },
      ...opts,
    })
    expect(w.get('input').attributes('id')).toBe('pw')
    expect(w.get('input').attributes('autocomplete')).toBe('current-password')
    expect(w.get('input').attributes('required')).toBeDefined()
    expect(w.find('div').attributes('id')).toBeUndefined()
    expect(w.find('div').attributes('required')).toBeUndefined()
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

  it('renders the vendored input and button atoms', () => {
    const w = mount(PasswordInput, { props: { modelValue: 'secret' }, ...opts })
    expect(w.find('[data-slot="input"]').exists()).toBe(true)
    expect(w.find('[data-slot="button"]').exists()).toBe(true)
  })

  it('has distinct, non-empty toggle labels in both shipped locales', () => {
    expect(en.login.showPassword).toBeTruthy()
    expect(en.login.hidePassword).toBeTruthy()
    expect(zhTW.login.showPassword).toBeTruthy()
    expect(zhTW.login.hidePassword).toBeTruthy()
    expect(zhTW.login.showPassword).not.toBe(en.login.showPassword)
    expect(zhTW.login.hidePassword).not.toBe(en.login.hidePassword)
  })
})
