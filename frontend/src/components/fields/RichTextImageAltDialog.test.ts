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
  // Cancel deliberately carries no data-cmd, so it is found by label (mirrors RichTextLinkDialog).
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

  // alt="" is the formal HTML way to mark an image as decorative, so an empty submit must go
  // through -- not be rejected as empty input the way RichTextLinkDialog's href field is.
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

  // Needs the true->false->true transition: build() mounts with open: true, which the reset watch
  // never sees. Without the reset, text typed for one image shows up in the next image's dialog.
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

  // Attached mount, unlike every other test here, and load-bearing: reka resolves the described-by
  // id with document.getElementById, which cannot see a detached wrapper -- mounted the usual way
  // this assertion fails whether or not the description exists, proving nothing.
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
