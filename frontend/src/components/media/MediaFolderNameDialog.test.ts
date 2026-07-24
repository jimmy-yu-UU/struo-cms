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

// Stub PrimeVue Dialog/InputText/Button to plain passthroughs (no teleport / no $primevue plugin
// instance needed), matching the pattern used elsewhere for dialog-hosting components (e.g.
// MediaUploadDialog.test.ts, MediaDetailDialog.test.ts).
const stubs = {
  Dialog: { name: 'Dialog', template: '<div v-if="visible"><slot /><slot name="footer" /></div>', props: ['visible'] },
  InputText: { name: 'InputText', template: '<input />', props: ['modelValue'] },
  Button: { name: 'Button', template: '<button><slot /></button>', props: ['label', 'disabled'] },
}

function mountDialog(props: { visible: boolean; header: string; initialName?: string }) {
  return mount(MediaFolderNameDialog, { props, global: { plugins: [i18n], stubs } })
}

describe('MediaFolderNameDialog', () => {
  it('shows the input prefilled with initialName when visible', () => {
    const w = mountDialog({ visible: true, header: 'Rename folder', initialName: 'Alpha' })
    const input = w.findComponent({ name: 'InputText' })
    expect(input.props('modelValue')).toBe('Alpha')
  })

  it('emits submit with the trimmed name and update:visible(false) on confirm', async () => {
    const w = mountDialog({ visible: true, header: 'New folder' })
    const input = w.findComponent({ name: 'InputText' })
    await input.vm.$emit('update:modelValue', '  New Name  ')
    const confirmBtn = w.findComponent({ name: 'Button' })
    await confirmBtn.trigger('click')
    expect(w.emitted('submit')).toEqual([['New Name']])
    expect(w.emitted('update:visible')).toEqual([[false]])
  })

  it('disables the confirm button when the name is blank', async () => {
    const w = mountDialog({ visible: true, header: 'New folder' })
    const confirmBtn = w.findComponent({ name: 'Button' })
    expect(confirmBtn.props('disabled')).toBe(true)
    const input = w.findComponent({ name: 'InputText' })
    await input.vm.$emit('update:modelValue', 'x')
    expect(w.findComponent({ name: 'Button' }).props('disabled')).toBe(false)
  })

  it('resets to blank when reopened without an initialName', async () => {
    const w = mountDialog({ visible: true, header: 'New folder', initialName: 'Alpha' })
    await w.setProps({ visible: false })
    await w.setProps({ visible: true, initialName: undefined })
    const input = w.findComponent({ name: 'InputText' })
    expect(input.props('modelValue')).toBe('')
  })
})
