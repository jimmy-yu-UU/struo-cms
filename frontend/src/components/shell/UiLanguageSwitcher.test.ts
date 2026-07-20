import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import UiLanguageSwitcher from './UiLanguageSwitcher.vue'
import { useUiLocaleStore } from '../../stores/uiLocaleStore'
import { i18n } from '../../i18n'

vi.mock('primevue/select', () => ({
  default: {
    name: 'Select',
    props: ['modelValue', 'options', 'optionLabel', 'optionValue'],
    emits: ['update:modelValue'],
    template:
      '<select class="lang-select" :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)">' +
      '<option v-for="o in options" :key="o.value" :value="o.value">{{ o.label }}</option></select>',
  },
}))

describe('UiLanguageSwitcher', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
  })

  it('renders both locale options and reflects the current locale', () => {
    const wrapper = mount(UiLanguageSwitcher, { global: { plugins: [i18n] } })
    const options = wrapper.findAll('option').map((o) => o.text())
    expect(options).toEqual(['繁體中文', 'English'])
    expect((wrapper.find('select.lang-select').element as HTMLSelectElement).value).toBe('zh-TW')
  })

  it('changing the select calls uiLocaleStore.set', async () => {
    const store = useUiLocaleStore()
    const spy = vi.spyOn(store, 'set')
    const wrapper = mount(UiLanguageSwitcher, { global: { plugins: [i18n] } })
    await wrapper.find('select.lang-select').setValue('en')
    expect(spy).toHaveBeenCalledWith('en')
  })
})
