import { describe, it, expect, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import ConfirmHost from './ConfirmHost.vue'
import { useConfirmStore } from '@/stores/confirmStore'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })

function mountHost() {
  // reka-ui's own portal wrapper component is itself named "Teleport" (it renders the real
  // <Teleport> only after mount). vue-test-utils' default `teleport` stub only special-cases
  // Vue's built-in Teleport; matching reka-ui's wrapper by name instead goes through the
  // generic stub path, which drops the default slot unless renderStubDefaultSlot is set.
  return mount(ConfirmHost, {
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

describe('ConfirmHost', () => {
  beforeEach(() => { setActivePinia(createPinia()) })

  // `stubs: { teleport: true }` keeps the dialog content inside the wrapper's own tree, so
  // assert through w.text() — with a real teleport the content would land on document.body
  // and the wrapper would look empty.
  //
  // Assert against the empty string, not just "doesn't contain one specific message": a
  // `not.toContain` on a single string would still pass even if the dialog rendered fully
  // with an empty body, since `request` is null while idle either way.
  it('renders nothing while no confirmation is pending', () => {
    expect(mountHost().text()).toBe('')
  })

  it('shows the message and header once a request opens', async () => {
    const w = mountHost()
    useConfirmStore().ask({ message: 'Delete this record?', header: 'Delete' })
    await flushPromises()
    expect(w.text()).toContain('Delete this record?')
    expect(w.text()).toContain('Delete')
  })

  it('falls back to the translated default header', async () => {
    const w = mountHost()
    useConfirmStore().ask({ message: 'Sure?' })
    await flushPromises()
    expect(w.text()).toContain(en.common.confirmDefaultHeader)
  })

  it('resolves false when reka-ui closes the dialog (escape only — outside-click is blocked by AlertDialog)', async () => {
    const w = mountHost()
    const p = useConfirmStore().ask({ message: 'Sure?' })
    await flushPromises()
    w.findComponent({ name: 'AlertDialog' }).vm.$emit('update:open', false)
    await expect(p).resolves.toBe(false)
  })

  // This is the acceptance criterion the earlier round shipped without: driving the actual
  // rendered button, not just asserting on text or synthesising update:open. reka's
  // AlertDialogAction is a DialogClose that fires its own onOpenChange(false) synchronously
  // ahead of a plain @click handler (mergeProps puts the component's own listener first),
  // which resolved every confirmation false — including "Confirm". Plain <button>s sidestep
  // that entirely; these tests are what would have caught it.
  it('clicking the rendered Confirm button resolves true', async () => {
    const w = mountHost()
    const p = useConfirmStore().ask({ message: 'Sure?' })
    await flushPromises()
    const confirmBtn = w.findAll('button').find((b) => b.text() === en.common.confirm)
    await confirmBtn?.trigger('click')
    await expect(p).resolves.toBe(true)
  })

  it('clicking the rendered Cancel button resolves false', async () => {
    const w = mountHost()
    const p = useConfirmStore().ask({ message: 'Sure?' })
    await flushPromises()
    const cancelBtn = w.findAll('button').find((b) => b.text() === en.common.cancel)
    await cancelBtn?.trigger('click')
    await expect(p).resolves.toBe(false)
  })
})
