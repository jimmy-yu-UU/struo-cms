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

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en: {
  settings: { title: 'Site Settings', branding: 'Branding', brandName: 'Site name', logo: 'Logo',
    uploadLogo: 'Upload new logo', removeLogo: 'Remove logo', save: 'Save changes', saved: 'Settings saved',
    saveFailed: 'Save failed', nameRequired: 'Site name is required', nameTooLong: 'too long',
    notPermitted: 'Admin role required' },
} } })

function mountView() {
  return mount(SettingsView, {
    global: {
      plugins: [i18n],
      stubs: { FilePicker: true, MediaUploadDropzone: true, PageHeader: true,
        Button: { template: '<button @click="$emit(\'click\')"><slot/></button>' },
        InputText: { props: ['modelValue'], template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)"/>' },
        Toast: true },
    },
  })
}

describe('SettingsView', () => {
  beforeEach(() => setActivePinia(createPinia()))

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
})
