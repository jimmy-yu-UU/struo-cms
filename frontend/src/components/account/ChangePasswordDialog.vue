<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import PasswordInput from '@/components/form/PasswordInput.vue'
import { usersApi } from '@/api/usersApi'
import { ApiError } from '@/api/apiClient'
import { generatePassword } from '@/lib/passwordGenerator'
import { tooManyRequestsMessage } from '@/lib/apiErrorMessage'
import { useAuthStore } from '@/stores/authStore'
import { useAppConfigStore } from '@/stores/appConfigStore'
import { useToast } from '@/composables/useToast'

const props = defineProps<{ open: boolean; targetUserId: string }>()
const emit = defineEmits<{ 'update:open': [boolean]; changed: [] }>()

const { t } = useI18n()
const auth = useAuthStore()
const appConfig = useAppConfigStore()
const toast = useToast()

// Derived, never passed in: the API decides self-vs-admin the same way, so deriving it here keeps the
// two from drifting. A super-admin opening this from their OWN user row lands in self mode and is
// correctly asked for their current password.
const isSelf = computed(() => props.targetUserId === auth.user?.id)

const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const currentError = ref('')
const newError = ref('')
const submitting = ref(false)

// Reset every field and error whenever the dialog opens, so a previous attempt's state never leaks
// into the next one.
watch(() => props.open, (open) => {
  if (!open) return
  currentPassword.value = ''
  newPassword.value = ''
  confirmPassword.value = ''
  currentError.value = ''
  newError.value = ''
})

function validate(): boolean {
  currentError.value = ''
  newError.value = ''
  if (newPassword.value.length < appConfig.passwordMinLength) {
    newError.value = t('password.tooShort', { min: appConfig.passwordMinLength })
    return false
  }
  if (newPassword.value !== confirmPassword.value) {
    newError.value = t('password.mismatch')
    return false
  }
  return true
}

async function onGenerate(): Promise<void> {
  const generated = generatePassword(appConfig.passwordMinLength)
  newPassword.value = generated
  confirmPassword.value = generated
  newError.value = ''
  try {
    await navigator.clipboard.writeText(generated)
    toast.add({ severity: 'success', summary: t('password.generatedAndCopied'), life: 2000 })
  } catch {
    // Never silent: the admin has to hand this value over, so a failed copy must be visible.
    toast.add({ severity: 'error', summary: t('password.copyFailed'), life: 3500 })
  }
}

async function onSubmit(): Promise<void> {
  if (!validate()) return
  submitting.value = true
  try {
    await usersApi.changePassword(props.targetUserId, {
      newPassword: newPassword.value,
      // Sent only in self mode, and never as an empty-string placeholder: the server treats a
      // present-but-wrong value as INVALID_CURRENT_PASSWORD, but an admin reset silently ignores
      // the key rather than rejecting it, so it must be entirely absent, not merely falsy.
      ...(isSelf.value ? { currentPassword: currentPassword.value } : {}),
    })
    toast.add({ severity: 'success', summary: t('password.changed'), life: 2000 })
    emit('changed')
    emit('update:open', false)
  } catch (e) {
    handleFailure(e)
  } finally {
    submitting.value = false
  }
}

// Field-level facts go on the field; whole-request refusals go to a toast.
function handleFailure(e: unknown): void {
  if (!(e instanceof ApiError)) {
    toast.add({ severity: 'error', summary: t('password.failed'), life: 3500 })
    return
  }
  switch (e.code) {
    case 'INVALID_CURRENT_PASSWORD':
      currentError.value = t('password.invalidCurrent')
      return
    case 'BAD_USER_INPUT':
      newError.value = t('password.tooShort', { min: appConfig.passwordMinLength })
      return
    case 'NO_LOCAL_PASSWORD':
      toast.add({ severity: 'error', summary: t('password.noLocalPassword'), life: 3500 })
      return
    case 'FORBIDDEN':
      toast.add({ severity: 'error', summary: t('password.forbidden'), life: 3500 })
      return
    case 'NOT_FOUND':
      toast.add({ severity: 'error', summary: t('password.notFound'), life: 3500 })
      return
    case 'TOO_MANY_REQUESTS': {
      const m = tooManyRequestsMessage(e)
      toast.add({ severity: 'error', summary: t(m.key, m.params ?? {}), life: 3500 })
      return
    }
    default:
      toast.add({ severity: 'error', summary: t('password.failed'), life: 3500 })
  }
}
</script>

<template>
  <Dialog :open="open" @update:open="(v: boolean) => emit('update:open', v)">
    <!-- max-w-lg here is BARE (DialogContent's own is sm:max-w-lg), so nothing needs to override it
         for this form's width. -->
    <DialogScrollContent class="max-w-lg">
      <DialogHeader>
        <DialogTitle>{{ isSelf ? t('password.changeTitle') : t('password.resetTitle') }}</DialogTitle>
        <DialogDescription v-if="!isSelf">{{ t('password.resetDescription') }}</DialogDescription>
      </DialogHeader>

      <form class="cpd-form" @submit.prevent="onSubmit">
        <label v-if="isSelf" class="cpd-field">
          <span>{{ t('password.current') }}</span>
          <PasswordInput
            v-model="currentPassword"
            autocomplete="current-password"
            :aria-invalid="!!currentError"
            :aria-describedby="currentError ? 'cpd-current-error' : undefined"
          />
          <p v-if="currentError" id="cpd-current-error" role="alert" class="cpd-error">{{ currentError }}</p>
        </label>

        <label class="cpd-field">
          <span>{{ t('password.new') }}</span>
          <PasswordInput
            v-model="newPassword"
            autocomplete="new-password"
            :aria-invalid="!!newError"
            :aria-describedby="newError ? 'cpd-new-error' : undefined"
          />
        </label>

        <label class="cpd-field">
          <span>{{ t('password.confirm') }}</span>
          <PasswordInput v-model="confirmPassword" autocomplete="new-password" />
        </label>

        <p v-if="newError" id="cpd-new-error" role="alert" class="cpd-error">{{ newError }}</p>

        <!-- Admin-reset mode only: a self-service user already knows a password, they don't need one
             generated for them. type="button" is load-bearing -- ui/button does not inject a type,
             and an untyped button inside this <form> would submit it instead. -->
        <Button v-if="!isSelf" type="button" variant="outline" @click="onGenerate">
          {{ t('password.generate') }}
        </Button>

        <DialogFooter>
          <Button type="button" variant="ghost" @click="emit('update:open', false)">
            {{ t('common.cancel') }}
          </Button>
          <Button type="submit" :disabled="submitting">
            {{ submitting ? t('password.submitting') : t('password.submit') }}
          </Button>
        </DialogFooter>
      </form>
    </DialogScrollContent>
  </Dialog>
</template>

<style scoped>
.cpd-form { display: grid; gap: 14px; }
.cpd-field { display: grid; gap: 4px; }
.cpd-field > span { font-size: .8rem; font-weight: 500; }
.cpd-error { margin: 0; color: var(--danger); font-size: .85rem; }
</style>
