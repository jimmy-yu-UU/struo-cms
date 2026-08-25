import { describe, it, expect, afterEach } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextLinkDialog from './RichTextLinkDialog.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: {
    fields: { richtext: {
      linkDialogTitle: 'Link settings', linkUrlLabel: 'Link URL',
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

function cancelButton(wrapper: VueWrapper) {
  // Cancel carries no data-cmd -- it is the one footer action that must never look like a
  // submit trigger, so find it by its rendered label instead.
  return wrapper.findAll('button').find((b) => b.text() === 'Cancel')
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

  // Every prefix of a valid URL is itself invalid ("h", "ht", "http", ...), so a live-computed
  // error would paint the field red on the very first keystroke of ordinary typing. Only a blur
  // (or an attempted submit, covered separately below) should surface the message.
  it('does not show an error while typing an incomplete URL, before the field is blurred', async () => {
    w = build({ href: '' })
    await w.get('[data-testid="href"]').setValue('ht')
    expect(w.find('[role="alert"]').exists()).toBe(false)
    expect(w.get('[data-testid="href"]').attributes('aria-invalid')).toBe('false')
  })

  it('shows the error once a rejected, non-empty value is blurred', async () => {
    w = build({ href: '' })
    const input = w.get('[data-testid="href"]')
    await input.setValue('javascript:alert(1)')
    expect(w.find('[role="alert"]').exists()).toBe(false) // not yet -- no blur
    await input.trigger('blur')
    const errorEl = w.get('[role="alert"]')
    expect(errorEl.text().length).toBeGreaterThan(0)
    expect(w.get('[data-testid="href"]').attributes('aria-invalid')).toBe('true')
    expect(w.get('[data-testid="href"]').attributes('aria-describedby')).toBe(errorEl.attributes('id'))
  })

  // Confirm is disabled for a rejected scheme, and the guard also lives in submit() itself
  // (mirroring RichTextTableSizeDialog's rationale) so a direct call gets the same refusal --
  // this is what actually proves the rejection, since a disabled button's click would already
  // be a no-op regardless of whether submit() itself refuses.
  it('rejects a disallowed scheme: no submit emitted, and an error tied to the input', async () => {
    w = build({ href: 'https://example.com' })
    const input = w.get('[data-testid="href"]')
    await input.setValue('javascript:alert(1)')
    await input.trigger('blur')
    const vm = w.vm as unknown as { submit: () => void }
    vm.submit()
    await w.vm.$nextTick()
    expect(w.emitted('submit')).toBeUndefined()
    expect(w.get('[data-cmd="linkSubmit"]').attributes('disabled')).toBeDefined()
    const errorEl = w.get('[role="alert"]')
    expect(input.attributes('aria-invalid')).toBe('true')
    expect(input.attributes('aria-describedby')).toBe(errorEl.attributes('id'))
    expect(errorEl.text().length).toBeGreaterThan(0)
  })

  it('does not emit submit for an empty href, and points at no error element', async () => {
    w = build({ href: '' })
    const vm = w.vm as unknown as { submit: () => void }
    vm.submit()
    await w.vm.$nextTick()
    expect(w.emitted('submit')).toBeUndefined()
    expect(w.get('[data-cmd="linkSubmit"]').attributes('disabled')).toBeDefined()
    expect(w.find('[role="alert"]').exists()).toBe(false)
    // Not just "no alert rendered" -- the input must stop referencing one. An aria-describedby
    // pointing at an id nothing renders is its own accessibility defect.
    expect(w.get('[data-testid="href"]').attributes('aria-describedby')).toBeUndefined()
  })

  it('Cancel closes without submitting, even with a valid href entered', async () => {
    w = build({ href: 'https://example.com' })
    const cancel = cancelButton(w)
    expect(cancel).toBeDefined()
    await cancel!.trigger('click')
    expect(w.emitted('update:open')).toEqual([[false]])
    expect(w.emitted('submit')).toBeUndefined()
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

  // Mirrors submit()'s own guard rationale (comment above it in the component): canRemove gates
  // whether the button renders, but a direct call -- through defineExpose, or a future caller --
  // must get the same refusal, not a remove the host never offered.
  it('refuses remove() when canRemove is false, even called directly', async () => {
    w = build({ href: 'https://example.com', canRemove: false })
    const vm = w.vm as unknown as { remove: () => void }
    vm.remove()
    await w.vm.$nextTick()
    expect(w.emitted('remove')).toBeUndefined()
    expect(w.emitted('update:open')).toBeUndefined()
  })

  it('gives every rendered button an explicit type="button", so mounting inside ItemForm\'s <form> cannot trigger a submit', () => {
    w = build({ href: 'https://example.com', canRemove: true })
    const buttons = w.findAll('button')
    expect(buttons.length).toBeGreaterThan(0)
    buttons.forEach((b) => expect(b.attributes('type')).toBe('button'))
  })
})
