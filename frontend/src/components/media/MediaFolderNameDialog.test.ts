import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaFolderNameDialog from './MediaFolderNameDialog.vue'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en: { media: { folderName: 'Folder name', folderConfirm: 'OK' } } },
})

// reka's own portal wrapper is itself named Teleport and collides with VTU's stub, dropping the
// dialog body; stubbing `teleport` with renderStubDefaultSlot keeps the content in the wrapper's
// own tree. No assertion here needs the content to reach document.body.
function mountDialog(props: { visible: boolean; header: string; initialName?: string }) {
  return mount(MediaFolderNameDialog, {
    props,
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

describe('MediaFolderNameDialog', () => {
  it('renders the vendored dialog and input, not PrimeVue ones', () => {
    const w = mountDialog({ visible: true, header: 'New folder' })
    expect(w.find('[data-slot="dialog-content"]').exists()).toBe(true)
    expect(w.find('[data-slot="input"]').exists()).toBe(true)
  })

  it('shows the input prefilled with initialName when visible', () => {
    const w = mountDialog({ visible: true, header: 'Rename folder', initialName: 'Alpha' })
    expect((w.get('input').element as HTMLInputElement).value).toBe('Alpha')
  })

  it('emits submit with the trimmed name and update:visible(false) on confirm', async () => {
    const w = mountDialog({ visible: true, header: 'New folder' })
    await w.get('input').setValue('  New Name  ')
    await w.get('[data-test="folder-name-confirm"]').trigger('click')
    expect(w.emitted('submit')).toEqual([['New Name']])
    expect(w.emitted('update:visible')).toEqual([[false]])
  })

  it('disables the confirm button when the name is blank', async () => {
    const w = mountDialog({ visible: true, header: 'New folder' })
    expect(w.get('[data-test="folder-name-confirm"]').attributes('disabled')).toBeDefined()
    await w.get('input').setValue('x')
    expect(w.get('[data-test="folder-name-confirm"]').attributes('disabled')).toBeUndefined()
  })

  // ui/button renders a bare <button> through reka's Primitive and injects no type, so HTML's own
  // type="submit" default applies. This dialog is not inside a <form> today, but the assertion
  // costs nothing and pins the convention if it ever is.
  it('gives the confirm button an explicit type="button"', () => {
    const w = mountDialog({ visible: true, header: 'New folder' })
    expect(w.get('[data-test="folder-name-confirm"]').attributes('type')).toBe('button')
  })

  // The visible<->open bridge's CLOSE direction (@update:open -> emit('update:visible', ...))
  // is untested by every other case here, which only drive the OPEN direction via props. Without
  // this handler the dialog can never be dismissed by Escape, the overlay, or this X, because
  // MediaLibraryView relies solely on update:visible(false) to clear its own state.
  it('emits update:visible(false) when the vendored close button (the X) is clicked', async () => {
    const w = mountDialog({ visible: true, header: 'New folder' })
    await w.get('[data-slot="dialog-close"]').trigger('click')
    expect(w.emitted('update:visible')).toEqual([[false]])
  })

  it('resets to blank when reopened without an initialName', async () => {
    const w = mountDialog({ visible: true, header: 'New folder', initialName: 'Alpha' })
    await w.setProps({ visible: false })
    await w.setProps({ visible: true, initialName: undefined })
    expect((w.get('input').element as HTMLInputElement).value).toBe('')
  })
})
