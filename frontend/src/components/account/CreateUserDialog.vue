<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import PasswordInput from '@/components/form/PasswordInput.vue'
import { usersApi, type CreatedUser } from '@/api/usersApi'
import { ApiError } from '@/api/apiClient'
import { tooManyRequestsMessage } from '@/lib/apiErrorMessage'
import { useAppConfigStore } from '@/stores/appConfigStore'
import { useToast } from '@/composables/useToast'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{ 'update:open': [boolean]; created: [CreatedUser] }>()

const { t } = useI18n()
const appConfig = useAppConfigStore()
const toast = useToast()

const email = ref('')
const name = ref('')
const password = ref('')
const confirmPassword = ref('')
const emailError = ref('')
const passwordError = ref('')
const submitting = ref(false)

// Reset in both directions: opening starts clean, and closing (cancel, Esc, success) drops the
// typed passwords instead of keeping them in a component that stays mounted with the list.
watch(() => props.open, (open) => {
  password.value = ''
  confirmPassword.value = ''
  if (!open) return
  email.value = ''
  name.value = ''
  emailError.value = ''
  passwordError.value = ''
})

function validate(): boolean {
  emailError.value = ''
  passwordError.value = ''
  if (email.value.trim().length === 0) {
    emailError.value = t('createUser.emailRequired')
    return false
  }
  if (password.value.length < appConfig.passwordMinLength) {
    passwordError.value = t('password.tooShort', { min: appConfig.passwordMinLength })
    return false
  }
  if (password.value !== confirmPassword.value) {
    passwordError.value = t('password.mismatch')
    return false
  }
  return true
}

async function onSubmit(): Promise<void> {
  if (submitting.value) return
  if (!validate()) return
  submitting.value = true
  try {
    const displayName = name.value.trim()
    const created = await usersApi.create({
      email: email.value.trim(),
      password: password.value,
      ...(displayName ? { name: displayName } : {}),
    })
    emit('created', created)
    emit('update:open', false)
  } catch (e) {
    handleFailure(e)
  } finally {
    submitting.value = false
  }
}

function handleFailure(e: unknown): void {
  if (e instanceof ApiError) {
    if (e.status === 409) {
      emailError.value = t('createUser.emailTaken')
      return
    }
    if (e.code === 'BAD_USER_INPUT') {
      // The server's message names the actual password rule (policy, max length); the maximum is
      // never published to this client, so that message is the only place the reason lives. Fall
      // back to the composed minimum-length text only if it ever arrives empty.
      passwordError.value = e.message || t('password.tooShort', { min: appConfig.passwordMinLength })
      return
    }
    if (e.code === 'FORBIDDEN') {
      toast.add({ severity: 'error', summary: t('createUser.forbidden'), life: 3500 })
      return
    }
    if (e.code === 'TOO_MANY_REQUESTS') {
      const m = tooManyRequestsMessage(e)
      toast.add({ severity: 'error', summary: t(m.key, m.params ?? {}), life: 3500 })
      return
    }
  }
  toast.add({ severity: 'error', summary: t('createUser.failed'), life: 3500 })
}
</script>

<template>
  <Dialog :open="open" @update:open="(v: boolean) => emit('update:open', v)">
    <DialogScrollContent>
      <DialogHeader>
        <DialogTitle>{{ t('createUser.title') }}</DialogTitle>
        <DialogDescription>{{ t('createUser.description') }}</DialogDescription>
      </DialogHeader>

      <form class="cud-form" @submit.prevent="onSubmit">
        <label class="cud-field" for="cud-email">
          <span>{{ t('createUser.email') }}</span>
          <Input
            id="cud-email"
            v-model="email"
            type="email"
            autocomplete="off"
            :aria-invalid="!!emailError"
            :aria-describedby="emailError ? 'cud-email-error' : undefined"
          />
        </label>
        <p v-if="emailError" id="cud-email-error" role="alert" class="cud-error">{{ emailError }}</p>

        <label class="cud-field" for="cud-name">
          <span>{{ t('createUser.name') }}</span>
          <Input id="cud-name" v-model="name" autocomplete="off" />
        </label>

        <label class="cud-field" for="cud-password">
          <span>{{ t('createUser.password') }}</span>
          <PasswordInput
            id="cud-password"
            v-model="password"
            autocomplete="new-password"
            :aria-invalid="!!passwordError"
            :aria-describedby="passwordError ? 'cud-password-error' : undefined"
          />
        </label>

        <label class="cud-field" for="cud-confirm">
          <span>{{ t('password.confirm') }}</span>
          <PasswordInput
            id="cud-confirm"
            v-model="confirmPassword"
            autocomplete="new-password"
            :aria-invalid="!!passwordError"
            :aria-describedby="passwordError ? 'cud-password-error' : undefined"
          />
        </label>
        <p v-if="passwordError" id="cud-password-error" role="alert" class="cud-error">{{ passwordError }}</p>

        <DialogFooter>
          <Button type="button" variant="ghost" @click="emit('update:open', false)">
            {{ t('common.cancel') }}
          </Button>
          <Button type="submit" :disabled="submitting">
            {{ submitting ? t('createUser.submitting') : t('createUser.submit') }}
          </Button>
        </DialogFooter>
      </form>
    </DialogScrollContent>
  </Dialog>
</template>

<style scoped>
.cud-form { display: grid; gap: 14px; }
.cud-field { display: grid; gap: 4px; }
.cud-field > span { font-size: .8rem; font-weight: 500; }
.cud-error { margin: 0; color: var(--danger); font-size: .85rem; }
</style>
