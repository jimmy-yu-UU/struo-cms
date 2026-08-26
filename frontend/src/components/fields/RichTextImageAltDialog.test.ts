import { describe, it, expect, afterEach, vi } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextImageAltDialog from './RichTextImageAltDialog.vue'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: {
    fields: { richtext: {
      altDialogTitle: 'Alt text', altDialogDescription: 'Describe this image for screen readers.',
      altLabel: 'Alt text',
    } },
    common: { cancel: 'Cancel', confirm: 'Confirm' },
  } },
})

let w: VueWrapper | null = null
afterEach(() => { w?.unmount(); w = null })

function build(props: Partial<{ open: boolean; alt: string }> = {}): VueWrapper {
  return mount(RichTextImageAltDialog, {
    props: { open: true, alt: '', ...props },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

function cancelButton(wrapper: VueWrapper) {
  // Cancel carries no data-cmd -- it is the one footer action that must never look like a
  // submit trigger, so find it by its rendered label instead (mirrors RichTextLinkDialog).
  return wrapper.findAll('button').find((b) => b.text() === 'Cancel')
}

describe('RichTextImageAltDialog', () => {
  it('seeds the input from the alt prop', () => {
    w = build({ alt: 'A red bicycle' })
    expect(w.get<HTMLInputElement>('[data-testid="alt"]').element.value).toBe('A red bicycle')
  })

  it('emits submit with the entered value and closes the dialog', async () => {
    w = build({ alt: 'A red bicycle' })
    await w.get('[data-testid="alt"]').setValue('A blue bicycle')
    await w.get('[data-cmd="altSubmit"]').trigger('click')
    expect(w.emitted('submit')).toEqual([['A blue bicycle']])
    expect(w.emitted('update:open')).toEqual([[false]])
  })

  // alt="" is the formal HTML way to mark an image as decorative, not a missing value -- clearing
  // the field and submitting must go through, not be treated as an empty-input rejection the way
  // RichTextLinkDialog's href field is.
  it('emits submit with an empty string when the field is cleared, and does not block it', async () => {
    w = build({ alt: 'A red bicycle' })
    await w.get('[data-testid="alt"]').setValue('')
    await w.get('[data-cmd="altSubmit"]').trigger('click')
    expect(w.emitted('submit')).toEqual([['']])
    expect(w.emitted('update:open')).toEqual([[false]])
  })

  it('Cancel closes without submitting, even with text entered', async () => {
    w = build({ alt: 'A red bicycle' })
    const cancel = cancelButton(w)
    expect(cancel).toBeDefined()
    await cancel!.trigger('click')
    expect(w.emitted('update:open')).toEqual([[false]])
    expect(w.emitted('submit')).toBeUndefined()
  })

  // build() always mounts with open: true, which a reset watch without `immediate` cannot cover --
  // only a true->false->true transition on an already-mounted instance exercises it. Without this,
  // text left over from editing one image would still be showing when a different image's dialog
  // opens with a different alt prop.
  it('resets to the props when reopened, not to the last edited text', async () => {
    w = build({ alt: 'A red bicycle' })
    await w.get('[data-testid="alt"]').setValue('something typed but never submitted')
    await w.setProps({ open: false })
    await w.setProps({ open: true, alt: 'A green tractor' })
    expect(w.get<HTMLInputElement>('[data-testid="alt"]').element.value).toBe('A green tractor')
  })

  it('gives every rendered button an explicit type="button", so mounting inside ItemForm\'s <form> cannot trigger a submit', () => {
    w = build({ alt: 'A red bicycle' })
    const buttons = w.findAll('button')
    expect(buttons.length).toBeGreaterThan(0)
    buttons.forEach((b) => expect(b.attributes('type')).toBe('button'))
  })

  // reka points DialogContent's aria-describedby at a DialogDescription id whether or not one is
  // rendered, and warns on mount when nothing in the document carries that id -- so the warning is
  // not cosmetic: without a description, assistive tech follows a dangling reference.
  //
  // Mounted attached, unlike every other test in this file, and that is load-bearing: reka resolves
  // the id with document.getElementById, which cannot see a detached wrapper. Mounted the usual way
  // this assertion fails whether or not the description exists, so it would prove nothing (mirrors
  // the last test in RichTextLinkDialog.test.ts).
  it('renders a description, so reka does not warn about a dangling aria-describedby', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const container = document.body.appendChild(document.createElement('div'))
    w = mount(RichTextImageAltDialog, {
      props: { open: true, alt: 'A red bicycle' },
      global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
      attachTo: container,
    })
    await flushPromises()
    const messages = warn.mock.calls.map((c) => c.map(String).join(' '))
    expect(messages.filter((m) => m.includes('Missing `Description`'))).toEqual([])
    container.remove()
  })
})
