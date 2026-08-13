import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import SettingsView from './SettingsView.vue'
import { useAppConfigStore } from '../stores/appConfigStore'
import { useAuthStore } from '../stores/authStore'
import type { ConfirmRequest } from '@/composables/useConfirm'

const toastAdd = vi.fn()
vi.mock('@/composables/useToast', () => ({ useToast: () => ({ add: toastAdd }) }))

// Capture the guard registered via onBeforeRouteLeave (same mocking approach as
// ItemFormView.test.ts) so tests can invoke it directly without a real router.
let leaveGuard: (() => Promise<boolean> | boolean) | null = null
vi.mock('vue-router', () => ({
  onBeforeRouteLeave: (guard: () => Promise<boolean> | boolean) => { leaveGuard = guard },
}))
const confirmRequire = vi.fn<(req: ConfirmRequest) => Promise<boolean>>(() => Promise.resolve(true))
vi.mock('@/composables/useConfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: {
  settings: { title: 'Site Settings', branding: 'Branding', brandName: 'Site name', logo: 'Logo',
    uploadLogo: 'Upload new logo', removeLogo: 'Remove logo', save: 'Save changes', saving: 'Saving…',
    saved: 'Settings saved', saveFailed: 'Save failed', nameRequired: 'Site name is required',
    nameTooLong: 'too long', notPermitted: 'Admin role required' },
  confirm: {
    unsavedHeader: 'Unsaved changes',
    unsavedMessage: 'You have unsaved changes. Leave this page and discard them?',
  },
} } })

function seedAdmin(): void {
  useAuthStore().user = { id: '1', isSuperAdmin: true, permissions: {} } as never
}

function mountView() {
  return mount(SettingsView, {
    global: {
      plugins: [i18n],
      stubs: { FilePicker: true, PageHeader: true,
        // Functional stub (not `true`) so tests can trigger the `uploaded` event onUploaded()
        // is wired to.
        MediaUploadDropzone: {
          emits: ['uploaded'],
          template: '<button data-test="upload" @click="$emit(\'uploaded\', { id: \'file-99\' })">up</button>',
        } },
    },
  })
}

describe('SettingsView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    confirmRequire.mockReset()
    confirmRequire.mockResolvedValue(true)
    toastAdd.mockClear()
    leaveGuard = null
  })

  it('shows a not-permitted state for non-super-admins', () => {
    useAuthStore().user = { id: '1', isSuperAdmin: false, permissions: {} } as never
    const wrapper = mountView()
    expect(wrapper.text()).toContain('Admin role required')
  })

  it('renders the vendored input and save button', async () => {
    seedAdmin()
    const w = mountView()
    await flushPromises()
    expect(w.find('[data-slot="input"]').exists()).toBe(true)
    // data-slot is the real migration guard: PrimeVue's own Button also renders a plain
    // type="button" <button>, so that attribute alone cannot distinguish the two.
    expect(w.get('[data-test="save"]').attributes('data-slot')).toBe('button')
    expect(w.get('[data-test="save"]').attributes('type')).toBe('button')
  })

  it('reflects the store brand name, including a value set after mount', async () => {
    seedAdmin()
    useAppConfigStore().brandName = 'Acme'
    const w = mountView()
    await flushPromises()
    const input = () => w.get('input[data-slot="input"]').element as HTMLInputElement
    expect(input().value).toBe('Acme')
    await w.get('input[data-slot="input"]').setValue('Acme Docs')
    expect(input().value).toBe('Acme Docs')
  })

  it('saves branding for a super-admin', async () => {
    seedAdmin()
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('input').setValue('New Brand')
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).toHaveBeenCalledWith({ brandName: 'New Brand', logoFileId: null })
  })

  it('blocks save when the name is empty', async () => {
    seedAdmin()
    const cfg = useAppConfigStore(); cfg.brandName = ''
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('input').setValue('   ')
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).not.toHaveBeenCalled()
  })

  it('blocks save and warns when the name exceeds 100 characters', async () => {
    seedAdmin()
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('input').setValue('a'.repeat(101))
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).not.toHaveBeenCalled()
    // Pin the specific branch: nameRequired and nameTooLong both warn, so also assert the message
    // so a future swap of the two guards can't leave this test green (i18n key stubbed at top).
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'warn', summary: 'too long' }))
  })

  it('swaps the save label while saving', async () => {
    seedAdmin()
    const w = mountView()
    await flushPromises()
    let release!: () => void
    vi.spyOn(useAppConfigStore(), 'saveBranding').mockReturnValue(new Promise<void>((r) => { release = r }))
    await w.get('input[data-slot="input"]').setValue('Acme')
    const pending = (w.vm as unknown as { save: () => Promise<void> }).save()
    await flushPromises()
    expect(w.get('[data-test="save"]').text()).toBe('Saving…')
    // :disabled is what actually stops a re-entrant save() on a double-click mid-save; the label
    // swap alone is cosmetic.
    expect(w.get('[data-test="save"]').attributes('disabled')).toBeDefined()
    release()
    await pending
  })

  // ---- unsaved-changes leave guard (mirrors ItemFormView's guard) -----------

  it('registers a route-leave guard synchronously on setup', () => {
    seedAdmin()
    mountView()
    expect(typeof leaveGuard).toBe('function')
  })

  it('lets navigation through when the form is clean, without asking', async () => {
    seedAdmin()
    mountView()
    await flushPromises()
    await expect(leaveGuard!()).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('blocks navigation when the confirmation is dismissed', async () => {
    seedAdmin()
    const w = mountView()
    await flushPromises()
    await w.get('input[data-slot="input"]').setValue('changed')
    // require() resolves false on cancel and on Escape — an AlertDialog is not backdrop-dismissible
    // (reka hard-prevents pointerDownOutside/interactOutside), so those are its only two paths to
    // false, and a single mock resolution covers both.
    confirmRequire.mockResolvedValueOnce(false)
    await expect(leaveGuard!()).resolves.toBe(false)
    expect(confirmRequire).toHaveBeenCalledWith(expect.objectContaining({ header: 'Unsaved changes' }))
  })

  it('allows navigation when the confirmation is accepted', async () => {
    seedAdmin()
    const w = mountView()
    await flushPromises()
    await w.get('input[data-slot="input"]').setValue('changed')
    confirmRequire.mockResolvedValueOnce(true)
    await expect(leaveGuard!()).resolves.toBe(true)
    expect(confirmRequire).toHaveBeenCalledWith(expect.objectContaining({ header: 'Unsaved changes' }))
  })

  it('a successful save re-baselines so leaving afterward does not prompt', async () => {
    seedAdmin()
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('input').setValue('New Brand') // now dirty
    await wrapper.find('[data-test="save"]').trigger('click') // save succeeds -> re-baseline

    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('does not prompt for a non-admin even if the underlying refs were somehow dirty', async () => {
    // Non-admins never see the editable inputs, but the guard explicitly checks isAdmin too
    // (spec: "while dirty (and admin)") as a defensive belt-and-suspenders check.
    useAuthStore().user = { id: '1', isSuperAdmin: false, permissions: {} } as never
    mountView()
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  // ---- logo flow (mount-time restore / upload / save failure) ----------

  it('recovers logoFileId from cfg.brandLogoUrl on mount and includes it in the save payload', async () => {
    seedAdmin()
    const cfg = useAppConfigStore()
    cfg.brandName = 'Old'
    cfg.brandLogoUrl = '/api/files/abc-123/content'
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('input').setValue('New Brand')
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).toHaveBeenCalledWith({ brandName: 'New Brand', logoFileId: 'abc-123' })
  })

  it('onUploaded sets logoFileId from the dropzone uploaded event and includes it in the save payload', async () => {
    seedAdmin()
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('[data-test="upload"]').trigger('click')
    await wrapper.find('input').setValue('New Brand')
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).toHaveBeenCalledWith({ brandName: 'New Brand', logoFileId: 'file-99' })
  })

  it('shows an error toast when saveBranding rejects', async () => {
    seedAdmin()
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    vi.spyOn(cfg, 'saveBranding').mockRejectedValue(new Error('boom'))
    const wrapper = mountView()
    await wrapper.find('input').setValue('New Brand')
    await wrapper.find('[data-test="save"]').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }))
  })
})
