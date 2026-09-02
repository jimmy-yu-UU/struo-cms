import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import FieldInput from './FieldInput.vue'
import FilePicker from './FilePicker.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import type { FieldMeta } from '../../types/schema'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: {
    noFileSelected: 'No file selected', selectFile: 'Select', clear: 'Clear', selectAFile: 'Select a file',
    searchFiles: 'Search files…', loadFilesFailed: 'Failed to load files.',
  } } },
})

function field(over: Partial<FieldMeta> = {}): FieldMeta {
  return { name: 'f', label: 'F', interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const stubs = {
  Input: { template: '<input class="stub-text" />' },
  Textarea: { template: '<textarea class="stub-textarea" />' },
  NumberField: { template: '<input class="stub-number" />' },
  Switch: { template: '<button role="switch" class="stub-switch" />' },
  DatePicker: { template: '<input class="stub-date" />' },
  Select: { template: '<div class="stub-select" role="combobox" />' },
  RadioGroup: { template: '<div class="stub-radio" />' },
}

describe('FieldInput', () => {
  it('renders the text field for the text interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'text' }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-text').exists()).toBe(true)
  })
  it('renders RichTextInput for richText', async () => {
    const w = mount(FieldInput, {
      props: { field: field({ interface: 'richText' }), modelValue: '' },
      global: { stubs: { ...stubs, RichTextInput: { template: '<div class="stub-richtext" />' } } },
    })
    // The async loader's own `import()` resolves only after Vitest's transform pipeline has
    // walked RichTextField -> RichTextInput -> TipTap/ProseMirror for the first time, which takes
    // real wall-clock time (measured ~200-300ms cold in this environment) -- far more than
    // `flushPromises()` (a single microtask/immediate-timer flush) advances. A fixed number of
    // `flushPromises()` calls was flaky here even at a high count, so poll with a short real delay
    // between checks instead, bounded well under the test timeout.
    for (let i = 0; i < 50 && !w.find('.stub-richtext').exists(); i++) {
      await new Promise((resolve) => setTimeout(resolve, 20))
      await flushPromises()
    }
    expect(w.find('.stub-richtext').exists()).toBe(true)
    expect(w.find('.stub-textarea').exists()).toBe(false)
  })
  it('renders Select for select interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'select', options: [{ value: 'a', label: 'A' }] }), modelValue: '' }, global: { stubs } })
    expect(w.find('.stub-select').exists()).toBe(true)
  })
  // This stub key matches components/form/DatePicker.vue's inferred component name (Vue infers it
  // from the filename), so it intercepts DateField's child the same way regardless of which
  // component that name resolves to.
  it('renders DatePicker for date interface', () => {
    // DateField itself (unlike the DatePicker child this stub key intercepts) is not stubbed, and
    // it calls useI18n() unconditionally in setup, so this mount needs a real i18n plugin present
    // even though the date interface never renders the time input whose label uses it.
    const w = mount(FieldInput, {
      props: { field: field({ interface: 'date' }), modelValue: null },
      global: { plugins: [i18n], stubs },
    })
    expect(w.find('.stub-date').exists()).toBe(true)
  })

  it('renders read-only display for unsupported interface', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'somethingNew' }), modelValue: '{}' }, global: { stubs } })
    expect(w.find('.readonly-field').exists()).toBe(true)
    expect(w.find('.stub-text').exists()).toBe(false)
  })

  it('binds maxlength on text inputs when the field declares one', () => {
    const w = mount(FieldInput, {
      props: { field: field({ interface: 'text', maxLength: 100 }), modelValue: '' },
    })
    expect(w.get('input').attributes('maxlength')).toBe('100')
  })

  it('omits maxlength when the field has none', () => {
    const w2 = mount(FieldInput, {
      props: { field: field({ interface: 'textarea', maxLength: null }), modelValue: '' },
    })
    expect(w2.get('textarea').attributes('maxlength')).toBeUndefined()
  })

  // ItemForm.vue generates a unique id per rendered control and needs it forwarded onto whatever
  // FieldInput dispatches to, so its <FieldLabel for> has something to actually point at.
  it('forwards the id prop onto the dispatched field component', () => {
    const w = mount(FieldInput, { props: { field: field({ interface: 'text' }), modelValue: '', id: 'my-id' }, global: { stubs } })
    expect(w.get('.stub-text').attributes('id')).toBe('my-id')
  })

  it('renders FilePicker for image interface and relays the value', async () => {
    setActivePinia(createPinia())
    const lang = useLanguageStore()
    lang.languages = [{ code: 'en', name: 'English', isDefault: true }]
    vi.spyOn(itemsApi, 'get').mockResolvedValue({ id: 'f1', fileName: 'a.png', contentType: 'image/png', size: 1 })
    const w = mount(FieldInput, {
      props: { field: field({ name: 'heroImageId', label: 'Hero', interface: 'image' }), modelValue: null },
      global: { plugins: [i18n], stubs: { ...stubs, Dialog: true, Button: true, MediaGrid: true } },
    })
    await flushPromises()
    const picker = w.findComponent(FilePicker)
    expect(picker.exists()).toBe(true)
    picker.vm.$emit('update:modelValue', 'f1')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual(['f1'])
  })

  // The dispatch-layer id lands in genuinely different places depending on the interface's
  // component: sometimes the real, focusable, labelable control (an association a screen reader
  // honours), sometimes only a wrapper div (legal HTML, but a `for` pointing at it never
  // associates with anything — that field keeps relying on its own aria-label instead). Verified
  // against the real vendored atoms, not stubs, since the earlier stub-based test only proves the
  // id is forwarded, not where it physically lands once it reaches reka's internals.
  describe('where the forwarded id actually lands (real atoms, not stubs)', () => {
    it('lands on the native <input> for a text field', () => {
      const w = mount(FieldInput, { props: { field: field({ interface: 'text' }), modelValue: '', id: 'my-id' } })
      expect(w.get('input').attributes('id')).toBe('my-id')
    })

    it('lands on the native <textarea> for a textarea field', () => {
      const w = mount(FieldInput, { props: { field: field({ interface: 'textarea' }), modelValue: '', id: 'my-id' } })
      expect(w.get('textarea').attributes('id')).toBe('my-id')
    })

    it('lands on the real switch button, not a wrapper, for a boolean field', () => {
      const w = mount(FieldInput, { props: { field: field({ interface: 'boolean' }), modelValue: false, id: 'my-id' } })
      expect(w.find('#my-id').attributes('role')).toBe('switch')
    })

    // reka's NumberFieldRoot declares `id` as its own prop (consuming it rather than letting it
    // fall through as a plain attribute) and threads it through internal context to
    // NumberFieldInput's real <input role="spinbutton">, not onto the root wrapper div it renders
    // itself — confirmed here rather than assumed from reading reka's source.
    it('lands on the real spinbutton input, not the outer wrapper, for a number field', () => {
      const w = mount(FieldInput, { props: { field: field({ interface: 'number' }), modelValue: 1, id: 'my-id' } })
      const byId = w.find('#my-id')
      expect(byId.exists()).toBe(true)
      expect(byId.attributes('role')).toBe('spinbutton')
    })

    // DateField's root is a plain <div> wrapping two separate controls (a date trigger button and
    // a time <input>); the id lands on that div, which is not itself a labelable element — the
    // for/id pairing is inert here, which is exactly why DateField carries its own aria-label.
    it('lands only on a non-labelable wrapper for a date field', () => {
      const w = mount(FieldInput, {
        props: { field: field({ interface: 'date' }), modelValue: null, id: 'my-id' },
        global: { plugins: [i18n] },
      })
      const byId = w.find('#my-id')
      expect(byId.exists()).toBe(true)
      expect(['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON']).not.toContain(byId.element.tagName)
    })
  })
})
