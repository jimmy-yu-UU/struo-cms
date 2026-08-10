import { describe, it, expect, beforeEach, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import UiLanguageSwitcher from './UiLanguageSwitcher.vue'
import { useUiLocaleStore } from '../../stores/uiLocaleStore'
import { i18n } from '../../i18n'

function mountSwitcher() {
  // reka-ui's Select portal wrapper is itself named "Teleport" (same hazard as ConfirmHost /
  // UserMenu) — stub it so SelectContent renders inline instead of vanishing from the tree.
  return mount(UiLanguageSwitcher, {
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

// reka-ui's SelectTrigger opens on pointerdown (not click) and SelectItem selects on
// pointerup (not click) — see reka-ui/dist/Select/{SelectTrigger,SelectItem}.js. A plain
// `.trigger('click')` never reaches either handler, so drive the real pointer events.
async function openSelect(wrapper: ReturnType<typeof mountSwitcher>) {
  await wrapper.find('[role="combobox"]').trigger('pointerdown')
  await flushPromises()
}

describe('UiLanguageSwitcher', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    localStorage.clear()
    i18n.global.locale.value = 'zh-TW'
  })

  it('renders both locale options and reflects the current locale', async () => {
    const wrapper = mountSwitcher()
    // SelectValue's label lookup depends on SelectItem registration, which — while the
    // dropdown is closed — happens inside a detached document fragment mounted in onMounted,
    // one tick after the initial synchronous render.
    await flushPromises()
    expect(wrapper.find('[role="combobox"]').text()).toContain('繁體中文')

    await openSelect(wrapper)
    const options = wrapper.findAll('[role="option"]').map((o) => o.text())
    expect(options).toEqual(['繁體中文', 'English'])
  })

  it('changing the select calls uiLocaleStore.set', async () => {
    const store = useUiLocaleStore()
    const spy = vi.spyOn(store, 'set')
    const wrapper = mountSwitcher()

    await openSelect(wrapper)
    const enOption = wrapper.findAll('[role="option"]').find((o) => o.text() === 'English')
    expect(enOption).toBeDefined()
    await enOption!.trigger('pointerup')
    await flushPromises()

    expect(spy).toHaveBeenCalledWith('en')
  })
})
