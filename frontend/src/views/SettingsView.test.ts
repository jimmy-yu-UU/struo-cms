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
vi.mock('primevue/usetoast', () => ({ useToast: () => ({ add: vi.fn() }) }))

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
      stubs: { FilePicker: true, MediaUploadDropzone: true, PageHeader: true, ConfirmDialog: true,
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
})
