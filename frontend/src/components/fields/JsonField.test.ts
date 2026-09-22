import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import JsonField from './JsonField.vue'
import type { FieldMeta } from '../../types/schema'

function field(over: Partial<FieldMeta> & { interface: string }): FieldMeta {
  return { name: 'f', label: 'F', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over } as FieldMeta
}

describe('JsonField', () => {
  it('initialises the textarea from the model value (pretty-printed)', () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } } })
    const ta = w.find('textarea')
    expect((ta.element as HTMLTextAreaElement).value).toContain('"a": 1')
  })

  it('emits the parsed value on valid JSON input', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: null } })
    await w.find('textarea').setValue('{"x": [1, 2]}')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([{ x: [1, 2] }])
  })

  it('emits null on a blank buffer', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } } })
    await w.find('textarea').setValue('   ')
    expect(w.emitted('update:modelValue')?.at(-1)).toEqual([null])
  })

  it('does not emit and shows an error on malformed JSON', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: null } })
    await w.find('textarea').setValue('{ not json')
    expect(w.emitted('update:modelValue')).toBeUndefined()
    expect(w.find('.json-error').exists()).toBe(true)
  })

  it('marks the textarea invalid via aria and announces the error', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } } })
    await w.get('textarea').setValue('{ not json')
    expect(w.get('textarea').attributes('aria-invalid')).toBe('true')
    // The error must be announced, not just coloured — colour alone is not an accessible error signal.
    expect(w.get('.json-error').attributes('role')).toBe('alert')
  })

  // aria-invalid is the only signal an assistive-tech user gets that the buffer is malformed;
  // nothing pins that it also clears once the buffer is fixed, so a regression that leaves it
  // stuck on "true" forever would read as a permanent, unexplained error.
  it('clears aria-invalid once the buffer becomes valid JSON again', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } } })
    await w.get('textarea').setValue('{ not json')
    expect(w.get('textarea').attributes('aria-invalid')).toBe('true')
    await w.get('textarea').setValue('{"a": 2}')
    expect(w.get('textarea').attributes('aria-invalid')).toBe('false')
  })

  // The root element is a wrapper <div>, not the control itself, so ItemForm's <label for>
  // lands on the wrapper and never reaches the textarea unless the textarea carries its own
  // accessible name matching the visible FieldLabel text.
  it('gives the textarea an accessible name matching the field label', () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json', label: 'Payload' }), modelValue: null } })
    expect(w.get('textarea').attributes('aria-label')).toBe('Payload')
  })

  it('renders the vendored textarea data-slot hook', () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } } })
    expect(w.find('[data-slot="textarea"]').exists()).toBe(true)
  })

  it('genuinely disables the textarea', () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 }, disabled: true } })
    expect(w.get('textarea').attributes('disabled')).toBe('')
  })

  // A post-mount prop change is the only assertion that distinguishes a prop-driven control from
  // one seeded once and then left alone; ItemFormView's "Reload latest" and the revisions drawer's
  // revert both replace the whole model after mount, so the buffer must follow a model swap too.
  it('reflects a post-mount model replacement', async () => {
    const w = mount(JsonField, { props: { field: field({ interface: 'json' }), modelValue: { a: 1 } } })
    expect((w.get('textarea').element as HTMLTextAreaElement).value).toContain('"a": 1')
    await w.setProps({ modelValue: { b: 2 } })
    expect((w.get('textarea').element as HTMLTextAreaElement).value).toContain('"b": 2')
  })
})
