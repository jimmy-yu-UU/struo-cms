import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils'
import { setActivePinia, createPinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import ItemFormView from './ItemFormView.vue'
import { itemsApi } from '../api/itemsApi'
import { ApiError } from '../api/apiClient'
import { useAuthStore } from '../stores/authStore'
import { useSchemaStore } from '../stores/schemaStore'
import { useLanguageStore } from '../stores/languageStore'
import type { ConfirmRequest } from '@/composables/useConfirm'
import { catalogs } from '../locales'

// The real ItemForm, FieldInput and JsonField are mounted: a JSON field's unparsable text must stop
// the save, count as an unsaved change, and survive a locale tab switch.
vi.mock('../api/rbacApi', () => ({ rbacApi: { getRolePermissions: vi.fn(), putRolePermissions: vi.fn(), getEffectivePermissions: vi.fn() } }))
const push = vi.fn()
let routeParams: Record<string, string> = {}
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: routeParams, name: 'collection-item' }),
  useRouter: () => ({ push }),
  onBeforeRouteLeave: () => {},
  onBeforeRouteUpdate: () => {},
}))
const confirmRequire = vi.fn<(req: ConfirmRequest) => Promise<boolean>>(() => Promise.resolve(true))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: vi.fn() }) }))

function f(name: string, iface: string, over: Record<string, unknown> = {}) {
  return { name, label: name, interface: iface, required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 1, isSystem: false, ...over }
}
const meta = { name: 'article', label: 'Article', fields: [
  f('status', 'text'),
  f('config', 'json'),
  f('extra', 'json', { translatable: true }),
], relations: [] }

const item = {
  id: '5', version: 3, status: 'draft', config: { a: 1 },
  translations: { en: { extra: { k: 1 } }, fr: { extra: { k: 2 } } },
}

const i18n = createI18n({ legacy: false, locale: 'en', fallbackLocale: 'en', messages: catalogs as never })
type Wrapper = VueWrapper<InstanceType<typeof ItemFormView>>

async function mountEdit(formMeta: typeof meta = meta): Promise<Wrapper> {
  routeParams = { name: 'article', id: '5' }
  const auth = useAuthStore()
  auth.user = { id: '1', isSuperAdmin: true, permissions: {} }
  const schema = useSchemaStore()
  schema.load = vi.fn().mockResolvedValue(undefined)
  schema.get = vi.fn().mockReturnValue(formMeta) as never
  const lang = useLanguageStore()
  lang.load = vi.fn().mockResolvedValue(undefined)
  lang.languages = [{ code: 'en', name: 'English', isDefault: true }, { code: 'fr', name: 'French', isDefault: false }]
  const w = mount(ItemFormView, {
    attachTo: document.body,
    global: { plugins: [i18n], stubs: { RevisionHistoryDrawer: true, teleport: true }, renderStubDefaultSlot: true },
  }) as Wrapper
  await flushPromises()
  return w
}

function textareas(w: Wrapper): HTMLTextAreaElement[] {
  return w.findAll('textarea').map((t) => t.element as HTMLTextAreaElement)
}
async function typeInto(w: Wrapper, index: number, text: string): Promise<void> {
  await w.findAll('textarea')[index].setValue(text)
}
async function clickSave(w: Wrapper): Promise<void> {
  const save = w.findAll('button').find((b) => b.text() === 'Save')!
  await save.trigger('click')
  await flushPromises()
  await new Promise((r) => setTimeout(r, 0))
}
async function openTab(w: Wrapper, name: string): Promise<void> {
  const tab = w.findAll('[role="tab"]').find((t) => t.text().includes(name))!
  await tab.trigger('mousedown')
  await flushPromises()
}

describe('ItemFormView JSON drafts', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    push.mockClear()
    confirmRequire.mockReset(); confirmRequire.mockResolvedValue(true)
    vi.spyOn(itemsApi, 'get').mockResolvedValue(structuredClone(item))
  })
  afterEach(() => vi.restoreAllMocks())

  it('shows the model text first, so a plain load is clean', async () => {
    const w = await mountEdit()
    // order: default-locale translatable tab first, then shared fields
    expect(textareas(w)[1].value).toContain('"a": 1')
    expect(await w.vm.guardLeave()).toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('does not send the save, reports the field, and focuses it when the text does not parse', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 4 })
    const w = await mountEdit()
    await typeInto(w, 1, '{"a":1,}')
    await clickSave(w)
    expect(update).not.toHaveBeenCalled()
    const alerts = w.findAll('[role="alert"]').map((a) => a.text()).filter((t) => t !== '')
    expect(alerts).toHaveLength(1)
    expect(document.activeElement).toBe(textareas(w)[1])
    expect(push).not.toHaveBeenCalled()
  })

  it('shows one error text on the field and keeps aria-invalid and aria-describedby pointing at it', async () => {
    const w = await mountEdit()
    await typeInto(w, 1, '{"a":1,}')
    await clickSave(w)
    const area = w.findAll('textarea')[1]
    expect(area.attributes('aria-invalid')).toBe('true')
    const target = document.getElementById(area.attributes('aria-describedby')!)
    expect(target).not.toBeNull()
    expect(target!.textContent!.length).toBeGreaterThan(0)
    expect(w.findAll('.json-error')).toHaveLength(0)
  })

  it('saves once the text parses again', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 4 })
    const w = await mountEdit()
    await typeInto(w, 1, '{"a":1,}')
    await typeInto(w, 1, '{"a":2}')
    await clickSave(w)
    expect(update).toHaveBeenCalledTimes(1)
    expect(update.mock.calls[0][2]).toMatchObject({ config: { a: 2 } })
  })

  it('counts a first edit that is unparsable as an unsaved change for leave and unload', async () => {
    const w = await mountEdit()
    await typeInto(w, 1, '{a:1}')
    confirmRequire.mockResolvedValue(false)
    expect(await w.vm.guardLeave()).toBe(false)
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    const ev = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(ev)
    expect(ev.defaultPrevented).toBe(true)
  })

  it('stays clean for unload while the text parses and equals the loaded value', async () => {
    await mountEdit()
    const ev = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(ev)
    expect(ev.defaultPrevented).toBe(false)
  })

  it('jumps to the locale tab that holds the unparsable field on save', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 4 })
    const w = await mountEdit()
    await openTab(w, 'French')
    await typeInto(w, 0, '{')
    await openTab(w, 'English')
    await clickSave(w)
    expect(update).not.toHaveBeenCalled()
    const active = w.findAll('[role="tab"]').find((t) => t.attributes('data-state') === 'active')!
    expect(active.text()).toContain('French')
    expect(document.activeElement).toBe(textareas(w)[0])
  })

  it('keeps the text and the error when the locale tab is left and re-entered', async () => {
    const w = await mountEdit()
    await openTab(w, 'French')
    await typeInto(w, 0, '{"k":')
    await openTab(w, 'English')
    await openTab(w, 'French')
    expect(textareas(w)[0].value).toBe('{"k":')
    expect(w.findAll('[role="alert"]').filter((a) => a.text() !== '')).toHaveLength(1)
    expect(w.findAll('textarea')[0].attributes('aria-invalid')).toBe('true')
  })

  it('discards the draft on Reload latest: text back to the server copy, nothing left to confirm', async () => {
    const update = vi.spyOn(itemsApi, 'update')
      .mockRejectedValueOnce(new ApiError(409, 'conflict', 'VERSION_CONFLICT'))
    const w = await mountEdit()
    vi.mocked(itemsApi.get).mockResolvedValue({ ...structuredClone(item), version: 4, config: { a: 9 } })
    await w.find('input').setValue('edited')
    await clickSave(w)
    expect(update).toHaveBeenCalledTimes(1)
    await typeInto(w, 1, '{')
    expect(w.text()).toContain('JSON')
    w.vm.reloadLatest()
    await flushPromises()
    expect(textareas(w)[1].value).toContain('"a": 9')
    expect(w.findAll('.json-error')).toHaveLength(0)
    expect(w.text()).not.toContain('JSON')
    expect(await w.vm.guardLeave()).toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('discards the draft when the item is re-fetched after a revert', async () => {
    const w = await mountEdit()
    await typeInto(w, 1, '{')
    vi.mocked(itemsApi.get).mockResolvedValue({ ...structuredClone(item), config: { a: 7 } })
    await w.vm.onReverted()
    await flushPromises()
    expect(textareas(w)[1].value).toContain('"a": 7')
    expect(await w.vm.guardLeave()).toBe(true)
  })

  it('discards the draft when the item is loaded again', async () => {
    const w = await mountEdit()
    await typeInto(w, 1, '{')
    await w.vm.init()
    await flushPromises()
    expect(await w.vm.guardLeave()).toBe(true)
    expect(w.findAll('.json-error')).toHaveLength(0)
  })

  describe('the error follows the text in the box', () => {
    const parseMessage = (text: string): string => {
      try { JSON.parse(text) } catch (e) { return (e as Error).message }
      return ''
    }
    const alertTexts = (w: Wrapper): string[] =>
      w.findAll('[role="alert"]').map((a) => a.text()).filter((x) => x !== '')

    it('replaces, then removes, the message as the text changes after a blocked save', async () => {
      const w = await mountEdit()
      await typeInto(w, 1, '{"a":1,}')
      await clickSave(w)
      expect(alertTexts(w)).toEqual([parseMessage('{"a":1,}')])

      await typeInto(w, 1, '{"a":2}')
      expect(alertTexts(w)).toEqual([])
      expect(w.findAll('textarea')[1].attributes('aria-invalid')).not.toBe('true')

      await typeInto(w, 1, '[[[')
      expect(parseMessage('[[[')).not.toBe(parseMessage('{"a":1,}'))
      expect(alertTexts(w)).toEqual([parseMessage('[[[')])
      expect(w.findAll('textarea')[1].attributes('aria-invalid')).toBe('true')
    })

    it('does not bring a server error back when the text parses again, and the save brought the field into view', async () => {
      vi.spyOn(itemsApi, 'update').mockRejectedValue(
        new ApiError(400, 'bad', 'BAD_USER_INPUT', [{ field: 'config', message: 'Server says no' }]))
      const w = await mountEdit()
      await clickSave(w)
      expect(alertTexts(w)).toEqual(['Server says no'])
      expect(document.activeElement).toBe(textareas(w)[1])
      await typeInto(w, 1, '{')
      await typeInto(w, 1, '{"a":3}')
      expect(alertTexts(w)).toEqual([])
      expect(w.findAll('textarea')[1].attributes('aria-invalid')).not.toBe('true')
    })

    it('does not bring a required error back when the text parses again', async () => {
      const required = { ...meta, fields: [f('status', 'text'), f('config', 'json', { required: true }), f('extra', 'json', { translatable: true })] }
      vi.mocked(itemsApi.get).mockResolvedValue({ ...structuredClone(item), config: null })
      const update = vi.spyOn(itemsApi, 'update').mockResolvedValue({ id: '5', version: 4 })
      const w = await mountEdit(required)
      await clickSave(w)
      expect(update).not.toHaveBeenCalled()
      expect(alertTexts(w)).toHaveLength(1)
      await typeInto(w, 1, '{')
      await typeInto(w, 1, '{"a":3}')
      expect(alertTexts(w)).toEqual([])
      expect(w.findAll('textarea')[1].attributes('aria-invalid')).not.toBe('true')
      await clickSave(w)
      expect(update).toHaveBeenCalledTimes(1)
    })

    it('brings a client validation error on another tab into view when saving', async () => {
      const required = { ...meta, fields: [f('status', 'text'), f('config', 'json'), f('extra', 'json', { translatable: true, required: true })] }
      vi.mocked(itemsApi.get).mockResolvedValue({ ...structuredClone(item), translations: { en: { extra: null }, fr: { extra: { k: 2 } } } })
      const w = await mountEdit(required)
      await openTab(w, 'French')
      await clickSave(w)
      const active = w.findAll('[role="tab"]').find((x) => x.attributes('data-state') === 'active')!
      expect(active.text()).toContain('English')
      expect(document.activeElement).toBe(textareas(w)[0])
    })

    it('clears a stale server banner when a draft or a validation error blocks the next save', async () => {
      vi.spyOn(itemsApi, 'update').mockRejectedValue(new Error('boom'))
      const w = await mountEdit()
      await clickSave(w)
      expect(w.find('p.error').exists()).toBe(true)
      await typeInto(w, 1, '{')
      await clickSave(w)
      expect(w.find('p.error').exists()).toBe(false)
      expect(alertTexts(w)).toHaveLength(1)
    })

    it('shows no error before the first save while the text parses, and one once it does not', async () => {
      const w = await mountEdit()
      expect(alertTexts(w)).toEqual([])
      await typeInto(w, 1, '{')
      expect(alertTexts(w)).toEqual([parseMessage('{')])
    })

    it('does not move the tab or the focus while typing after a blocked save', async () => {
      const w = await mountEdit()
      await openTab(w, 'French')
      await typeInto(w, 0, '{')
      await clickSave(w)
      await openTab(w, 'English')
      const area = textareas(w)[1]
      area.focus()
      await typeInto(w, 1, '[')
      await flushPromises()
      const active = w.findAll('[role="tab"]').find((x) => x.attributes('data-state') === 'active')!
      expect(active.text()).toContain('English')
      expect(document.activeElement).toBe(area)
    })

    it('shows the live parse error, once, on a field that also has a server error', async () => {
      vi.spyOn(itemsApi, 'update').mockRejectedValue(
        new ApiError(400, 'bad', 'BAD_USER_INPUT', [{ field: 'config', message: 'Server says no' }]))
      const w = await mountEdit()
      await clickSave(w)
      expect(alertTexts(w)).toEqual(['Server says no'])
      await typeInto(w, 1, '{')
      expect(alertTexts(w)).toEqual([parseMessage('{')])
      expect(w.findAll('textarea')[1].attributes('aria-invalid')).toBe('true')
    })
  })

  it('a null JSON value stays null after Reload latest, with or without an unparsable text in the box', async () => {
    const nullConfig = { ...structuredClone(item), config: null, version: 4 }
    vi.mocked(itemsApi.get).mockResolvedValue({ ...structuredClone(item), config: null })
    vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'conflict', 'VERSION_CONFLICT'))
    const w = await mountEdit()
    expect(textareas(w)[1].value).toBe('')
    vi.mocked(itemsApi.get).mockResolvedValue(nullConfig)
    await w.find('input').setValue('edited')
    await typeInto(w, 1, '{"a":1}')
    await clickSave(w)
    await typeInto(w, 1, '{')
    w.vm.reloadLatest()
    await flushPromises()
    expect(textareas(w)[1].value).toBe('')
    expect((w.vm as unknown as { model: { shared: { config: unknown } } }).model.shared.config).toBeNull()
    expect(w.text()).not.toContain('JSON')
    expect(await w.vm.guardLeave()).toBe(true)
  })

  it('keeps the edits through the 409 recovery, and holds a text typed after it as a draft', async () => {
    const update = vi.spyOn(itemsApi, 'update').mockRejectedValueOnce(new ApiError(409, 'conflict', 'VERSION_CONFLICT'))
    const w = await mountEdit()
    await w.find('input').setValue('edited')
    await clickSave(w)
    expect(update).toHaveBeenCalledTimes(1)
    expect((w.vm as unknown as { conflict: boolean }).conflict).toBe(true)
    expect((w.find('input').element as HTMLInputElement).value).toBe('edited')
    await typeInto(w, 1, '{"a":')
    await clickSave(w)
    expect(update).toHaveBeenCalledTimes(1)
    expect(w.text()).toContain('JSON')
    expect(textareas(w)[1].value).toBe('{"a":')
  })

  it('disables the inputs while a save runs, so no text can appear between the request and the navigation', async () => {
    let release!: () => void
    vi.spyOn(itemsApi, 'update').mockImplementation(() => new Promise((r) => { release = () => r({ id: '5', version: 4 }) }))
    const w = await mountEdit()
    const saving = (w.vm as unknown as { onSubmit: () => Promise<void> }).onSubmit()
    await flushPromises()
    expect(w.findAll('textarea').every((x) => x.attributes('disabled') !== undefined)).toBe(true)
    release()
    await saving
  })

  it('puts no draft attribute on the DOM of fields that parse nothing', async () => {
    const w = await mountEdit()
    await typeInto(w, 1, '{')
    expect(w.findAll('*').filter((el) => 'draft' in el.attributes())).toHaveLength(0)
  })
})
