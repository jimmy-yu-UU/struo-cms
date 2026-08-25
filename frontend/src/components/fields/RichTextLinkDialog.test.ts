import { describe, it, expect, afterEach } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextLinkDialog from './RichTextLinkDialog.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: {
    fields: { richtext: {
      link: 'Link', linkPrompt: 'Link URL',
      linkOpenInNewTab: 'Open in new tab', removeLink: 'Remove link',
      linkUrlInvalid: 'Enter a valid http(s) or mailto link.',
    } },
    // Real path is common.cancel / common.confirm, not top-level -- confirmed against
    // frontend/src/locales/en.ts before wiring the component to it.
    common: { cancel: 'Cancel', confirm: 'Confirm' },
  } },
})

let w: VueWrapper | null = null
afterEach(() => { w?.unmount(); w = null })

function build(props: Partial<{ open: boolean; href: string; newTab: boolean; canRemove: boolean }> = {}): VueWrapper {
  return mount(RichTextLinkDialog, {
    props: { open: true, href: '', newTab: false, canRemove: false, ...props },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

describe('RichTextLinkDialog', () => {
  it('seeds the href input and the checkbox from the open props', () => {
    w = build({ href: 'https://example.com', newTab: true })
    expect(w.get<HTMLInputElement>('[data-testid="href"]').element.value).toBe('https://example.com')
    // reka's CheckboxRoot renders as role="checkbox" with a data-state attribute, not a native
    // checked property -- this is what a wrong initial newTab value would actually get wrong.
    expect(w.get('[role="checkbox"]').attributes('data-state')).toBe('checked')
  })

  it('seeds newTab false as unchecked', () => {
    w = build({ href: 'https://example.com', newTab: false })
    expect(w.get('[role="checkbox"]').attributes('data-state')).toBe('unchecked')
  })

  // build() always mounts with open: true, which the reset watch (not `immediate`) cannot cover
  // -- only a true->false->true transition on an already-mounted instance exercises it. Without
  // this, a rejected attempt left in the input would still be showing the next time the dialog
  // for a *different* link opens with different props.
  it('resets to the props when reopened, not to the last rejected attempt', async () => {
    w = build({ href: 'https://example.com', newTab: false })
    await w.get('[data-testid="href"]').setValue('javascript:alert(1)')
    await w.setProps({ open: false })
    await w.setProps({ open: true, href: 'https://other.example', newTab: true })
    expect(w.get<HTMLInputElement>('[data-testid="href"]').element.value).toBe('https://other.example')
    expect(w.get('[role="checkbox"]').attributes('data-state')).toBe('checked')
  })

  it('emits submit with the entered href and the checkbox state', async () => {
    w = build({ href: 'https://example.com', newTab: false })
    await w.get('[data-testid="href"]').setValue('https://updated.example')
    await w.get('[role="checkbox"]').trigger('click')
    await w.get('[data-cmd="linkSubmit"]').trigger('click')
    expect(w.emitted('submit')).toEqual([[{ href: 'https://updated.example', newTab: true }]])
    expect(w.emitted('update:open')).toEqual([[false]])
  })

  // Confirm is disabled for a rejected scheme, and the guard also lives in submit() itself
  // (mirroring RichTextTableSizeDialog's rationale) so a direct call gets the same refusal --
  // this is what actually proves the rejection, since a disabled button's click would already
  // be a no-op regardless of whether submit() itself refuses.
  it('rejects a disallowed scheme: no submit emitted, and an error tied to the input', async () => {
    w = build({ href: 'https://example.com' })
    await w.get('[data-testid="href"]').setValue('javascript:alert(1)')
    const vm = w.vm as unknown as { submit: () => void }
    vm.submit()
    await w.vm.$nextTick()
    expect(w.emitted('submit')).toBeUndefined()
    expect(w.get('[data-cmd="linkSubmit"]').attributes('disabled')).toBeDefined()
    const errorEl = w.get('[role="alert"]')
    const input = w.get('[data-testid="href"]')
    expect(input.attributes('aria-invalid')).toBe('true')
    expect(input.attributes('aria-describedby')).toBe(errorEl.attributes('id'))
    expect(errorEl.text().length).toBeGreaterThan(0)
  })

  it('does not emit submit for an empty href, and shows no error for it', async () => {
    w = build({ href: '' })
    const vm = w.vm as unknown as { submit: () => void }
    vm.submit()
    await w.vm.$nextTick()
    expect(w.emitted('submit')).toBeUndefined()
    expect(w.get('[data-cmd="linkSubmit"]').attributes('disabled')).toBeDefined()
    expect(w.find('[role="alert"]').exists()).toBe(false)
  })

  it('offers Remove only when canRemove is true, and it emits remove', async () => {
    w = build({ href: 'https://example.com', canRemove: false })
    expect(w.find('[data-cmd="linkRemove"]').exists()).toBe(false)

    await w.setProps({ canRemove: true })
    await w.get('[data-cmd="linkRemove"]').trigger('click')
    expect(w.emitted('remove')).toEqual([[]])
    expect(w.emitted('update:open')).toEqual([[false]])
    // Removing must not also fabricate a submit -- Task 3's command dispatches on which of the
    // two resolves, and a stray submit would apply a link the caller asked to remove.
    expect(w.emitted('submit')).toBeUndefined()
  })

  it('gives every rendered button an explicit type="button", so mounting inside ItemForm\'s <form> cannot trigger a submit', () => {
    w = build({ href: 'https://example.com', canRemove: true })
    const buttons = w.findAll('button')
    expect(buttons.length).toBeGreaterThan(0)
    buttons.forEach((b) => expect(b.attributes('type')).toBe('button'))
  })
})
