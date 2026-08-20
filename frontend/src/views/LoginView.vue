<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { storeToRefs } from 'pinia'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import PasswordInput from '@/components/form/PasswordInput.vue'
import { useAuthStore } from '../stores/authStore'
import { useAppConfigStore } from '../stores/appConfigStore'
import { apiBaseUrl, ApiError } from '../api/apiClient'
import { tooManyRequestsMessage } from '@/lib/apiErrorMessage'
import BrandMark from '../components/shell/BrandMark.vue'

const email = ref('')
const password = ref('')
const error = ref('')
const submitting = ref(false)

const auth = useAuthStore()
const router = useRouter()
const { t } = useI18n()
const appConfig = useAppConfigStore()
const { brandName, oidcEnabled } = storeToRefs(appConfig)

const submitLabel = computed(() => (submitting.value ? t('login.submitting') : t('login.submit')))

async function onSubmit() {
  error.value = ''
  submitting.value = true
  try {
    await auth.login(email.value, password.value)
    router.push({ name: 'dashboard' })
  } catch (e) {
    error.value = loginErrorMessage(e)
  } finally {
    submitting.value = false
  }
}

// The server's prose is English-only and deliberately vague, so it is never displayed: map the
// machine-readable code to a localized sentence and keep the server message as a last resort.
// UNAUTHORIZED covers both "wrong password" and "no such account" — that pair must stay
// indistinguishable (the account-enumeration surface), so they share one message here by design;
// a future maintainer should not "improve" this by splitting them back apart.
function loginErrorMessage(e: unknown): string {
  if (!(e instanceof ApiError)) return t('login.failed')
  switch (e.code) {
    case 'UNAUTHORIZED': return t('login.invalidCredentials')
    case 'ACCOUNT_INACTIVE': return t('login.accountInactive')
    case 'TOO_MANY_REQUESTS': {
      const m = tooManyRequestsMessage(e)
      return t(m.key, m.params ?? {})
    }
    default: return t('login.failed')
  }
}

function onSso() {
  window.location.href = `${apiBaseUrl}/auth/login/oidc?returnUrl=/`
}
</script>

<template>
  <div class="login-wrap">
    <div class="login-card rounded-xl">
      <div class="brand">
        <BrandMark />
        <b>{{ brandName }}</b>
        <span class="text-xs font-medium text-muted-foreground">{{ t('login.subtitle') }}</span>
      </div>

      <form class="login-form" @submit.prevent="onSubmit">
        <div class="field">
          <label for="lg-email" class="text-xs font-medium text-muted-foreground">{{ t('login.email') }}</label>
          <Input
            id="lg-email"
            v-model="email"
            type="email"
            autocomplete="username"
            required
            autofocus
          />
        </div>

        <div class="field">
          <label for="lg-pw" class="text-xs font-medium text-muted-foreground">{{ t('login.password') }}</label>
          <!-- No class here: PasswordInput has inheritAttrs: false, so a class would land on the
               inner input rather than the wrapper the show/hide toggle positions against. The
               wrapper is a block div in a grid cell and the vendored input is already w-full, so
               nothing is needed. id/autocomplete/required do reach the native input via $attrs. -->
          <PasswordInput
            id="lg-pw"
            v-model="password"
            autocomplete="current-password"
            required
          />
        </div>

        <Button type="submit" class="w-full" :disabled="submitting">{{ submitLabel }}</Button>

        <p v-if="error" class="error" role="alert">{{ error }}</p>

        <template v-if="oidcEnabled">
          <div class="divider text-muted-foreground" role="separator" :aria-label="t('login.or')">{{ t('login.or') }}</div>
          <Button
            type="button"
            variant="secondary"
            class="sso-btn w-full"
            data-test="sso"
            @click="onSso"
          >
            <svg width="17" height="17" viewBox="0 0 21 21" aria-hidden="true">
              <rect width="10" height="10" fill="#f25022" />
              <rect x="11" width="10" height="10" fill="#7fba00" />
              <rect y="11" width="10" height="10" fill="#00a4ef" />
              <rect x="11" y="11" width="10" height="10" fill="#ffb900" />
            </svg>
            <span>{{ t('login.ssoMicrosoft') }}</span>
          </Button>
        </template>
      </form>
    </div>
  </div>
</template>

<style scoped>
.login-wrap {
  min-height: 100dvh;
  display: grid;
  place-items: center;
  padding: 24px;
  background: var(--bg);
}
.login-card {
  width: min(420px, 100%);
  padding: 40px 36px 32px;
  display: grid;
  gap: 26px;
  background: var(--surface);
  border: 1px solid var(--border);
  box-shadow: var(--shadow-2);
}
.brand {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
}
.brand b {
  font-size: 1.05rem;
  letter-spacing: -0.01em;
  color: var(--fg);
}
.login-form {
  display: grid;
  gap: 18px;
}
.field {
  display: grid;
  gap: 6px;
}
.divider {
  display: flex;
  align-items: center;
  gap: 14px;
  font-size: 0.8rem;
  white-space: nowrap;
}
.divider::before,
.divider::after {
  content: '';
  flex: 1;
  height: 1px;
  background: var(--border);
}
.error {
  color: var(--danger);
  font-size: 0.85rem;
  margin: 0;
}
</style>
