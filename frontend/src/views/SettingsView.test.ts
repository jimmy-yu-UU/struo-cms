import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createI18n } from 'vue-i18n'
import SettingsView from './SettingsView.vue'
import { useAppConfigStore } from '../stores/appConfigStore'
import { useAuthStore } from '../stores/authStore'

// Real useToast() throws "No PrimeVue Toast provided!" without a ToastService
// provider (see node_modules/primevue/usetoast) — mock it like the codebase's
// other Toast-using component tests (MediaDetailDialog.test.ts, RevisionHistoryDrawer.test.ts).
// Hoisted so `add` is a stable, assertable spy (mirrors RevisionHistoryDrawer.test.ts) rather than
// a fresh vi.fn() handed out per useToast() call.
const toastAdd = vi.fn()
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: toastAdd }) }))

// Capture the guard registered via onBeforeRouteLeave (FE-5 pattern, same mocking approach as
// ItemFormView.test.ts) so tests can invoke it directly without a real router.
let leaveGuard: (() => Promise<boolean> | boolean) | null = null
vi.mock('vue-router', () => ({
  onBeforeRouteLeave: (guard: () => Promise<boolean> | boolean) => { leaveGuard = guard },
}))
const confirmRequire = vi.fn()
vi.mock('primevue/useconfirm', () => ({ useConfirm: () => ({ require: confirmRequire }) }))

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: {
  settings: { title: 'Site Settings', branding: 'Branding', brandName: 'Site name', logo: 'Logo',
    uploadLogo: 'Upload new logo', removeLogo: 'Remove logo', save: 'Save changes', saved: 'Settings saved',
    saveFailed: 'Save failed', nameRequired: 'Site name is required', nameTooLong: 'too long',
    notPermitted: 'Admin role required' },
  confirm: {
    unsavedHeader: 'Unsaved changes',
    unsavedMessage: 'You have unsaved changes. Leave this page and discard them?',
  },
} } })

function mountView() {
  return mount(SettingsView, {
    global: {
      plugins: [i18n],
      stubs: { FilePicker: true, PageHeader: true, ConfirmDialog: true,
        // Functional stub (not `true`) so tests can trigger the `uploaded` event onUploaded()
        // is wired to, mirroring the Button stub below.
        MediaUploadDropzone: {
          emits: ['uploaded'],
          template: '<button data-test="upload" @click="$emit(\'uploaded\', { id: \'file-99\' })">up</button>',
        },
        Button: { template: '<button @click="$emit(\'click\')"><slot/></button>' },
        InputText: { props: ['modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)"/>' },
        Toast: true },
    },
  })
}

describe('SettingsView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    confirmRequire.mockClear()
    toastAdd.mockClear()
    leaveGuard = null
  })

  it('shows a not-permitted state for non-super-admins', () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: false, permissions: {} } as never
    const wrapper = mountView()
    expect(wrapper.text()).toContain('Admin role required')
  })

  it('saves branding for a super-admin', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('input').setValue('New Brand')
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).toHaveBeenCalledWith({ brandName: 'New Brand', logoFileId: null })
  })

  it('blocks save when the name is empty', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
    const cfg = useAppConfigStore(); cfg.brandName = ''
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('input').setValue('   ')
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).not.toHaveBeenCalled()
  })

  // ---- unsaved-changes leave guard (spec §4.3/§5, mirrors ItemFormView's FE-5 guard) -----------

  it('registers a route-leave guard synchronously on setup', () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
    mountView()
    expect(typeof leaveGuard).toBe('function')
  })

  it('route-leave guard resolves true without confirming when the form is clean', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
    mountView()
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  it('editing the name marks the view dirty and the leave guard prompts for confirmation', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    const wrapper = mountView()
    await wrapper.find('input').setValue('New Brand')

    const p = Promise.resolve(leaveGuard!())
    expect(confirmRequire).toHaveBeenCalledTimes(1)
    expect(confirmRequire.mock.calls[0][0].header).toBe('Unsaved changes')
    confirmRequire.mock.calls[0][0].accept()
    await expect(p).resolves.toBe(true)

    const rejected = Promise.resolve(leaveGuard!())
    confirmRequire.mock.calls[1][0].reject()
    await expect(rejected).resolves.toBe(false)
  })

  it('a successful save re-baselines so leaving afterward does not prompt', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
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
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: false, permissions: {} } as never
    mountView()
    await expect(Promise.resolve(leaveGuard!())).resolves.toBe(true)
    expect(confirmRequire).not.toHaveBeenCalled()
  })

  // ---- logo flow (TEST-4: mount-time restore / upload / save failure / name-too-long) ----------

  it('recovers logoFileId from cfg.brandLogoUrl on mount and includes it in the save payload', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
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
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    const save = vi.spyOn(cfg, 'saveBranding').mockResolvedValue()
    const wrapper = mountView()
    await wrapper.find('[data-test="upload"]').trigger('click')
    await wrapper.find('input').setValue('New Brand')
    await wrapper.find('[data-test="save"]').trigger('click')
    expect(save).toHaveBeenCalledWith({ brandName: 'New Brand', logoFileId: 'file-99' })
  })

  it('shows an error toast when saveBranding rejects', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
    const cfg = useAppConfigStore(); cfg.brandName = 'Old'
    vi.spyOn(cfg, 'saveBranding').mockRejectedValue(new Error('boom'))
    const wrapper = mountView()
    await wrapper.find('input').setValue('New Brand')
    await wrapper.find('[data-test="save"]').trigger('click')
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(toastAdd).toHaveBeenCalledWith(expect.objectContaining({ severity: 'error' }))
  })

  it('blocks save and warns when the name exceeds 100 characters', async () => {
    const auth = useAuthStore(); auth.user = { id: '1', isSuperAdmin: true, permissions: {} } as never
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
})
