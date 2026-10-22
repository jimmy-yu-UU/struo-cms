<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { storeToRefs } from 'pinia'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import PasswordInput from '@/components/form/PasswordInput.vue'
import { useAuthStore } from '../stores/authStore'
import { useAppConfigStore } from '../stores/appConfigStore'
import { apiBaseUrl, ApiError } from '../api/apiClient'
import { tooManyRequestsMessage } from '@/lib/apiErrorMessage'
import { encodeRedirectParam, safeRedirect } from '@/lib/safeRedirect'
import { resolveSsoIcon } from '@/lib/ssoIcons'
import BrandMark from '../components/shell/BrandMark.vue'

const email = ref('')
const password = ref('')
const error = ref('')
const notice = ref('')
const submitting = ref(false)

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const { t } = useI18n()
const appConfig = useAppConfigStore()
const { brandName, oidcProviders } = storeToRefs(appConfig)

// Read once: clearing the notice params below rewrites the query, and sign-in must still return here.
const redirectTarget = safeRedirect(route.query.redirect)

const submitLabel = computed(() => (submitting.value ? t('login.submitting') : t('login.submit')))

async function onSubmit() {
  error.value = ''
  submitting.value = true
  try {
    await auth.login(email.value, password.value)
    router.push(redirectTarget ?? { name: 'dashboard' })
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

// The API ends a failed or refused external sign-in with /login?error=<code>. The code is looked up,
// never displayed: an unknown value gets the generic message. Inactive reuses the password-login text.
const SSO_ERROR_KEYS: Record<string, string> = {
  external_login_failed: 'login.ssoFailed',
  external_login_denied: 'login.ssoDenied',
  external_login_NoEmail: 'login.ssoNoEmail',
  external_login_EmailNotVerified: 'login.ssoEmailNotVerified',
  external_login_DomainNotAllowed: 'login.ssoDomainNotAllowed',
  external_login_TenantNotAllowed: 'login.ssoTenantNotAllowed',
  external_login_Inactive: 'login.accountInactive',
}

// Why the browser was sent here: ?reason=expired after a 401 ended a signed-in session,
// ?reason=logout-incomplete when the server could not confirm a sign-out. Unknown values are ignored.
const REASON_NOTICE_KEYS: Record<string, string> = {
  expired: 'login.sessionExpired',
  'logout-incomplete': 'user.logoutIncomplete',
}

function showReasonNotice(): boolean {
  const reason = route.query.reason
  if (typeof reason !== 'string' || !Object.hasOwn(REASON_NOTICE_KEYS, reason)) return false
  notice.value = t(REASON_NOTICE_KEYS[reason])
  return true
}

onMounted(() => {
  const code = route.query.error
  const hasReason = showReasonNotice()
  if (code === undefined && !hasReason) return
  if (code !== undefined) {
    const key = typeof code === 'string' && Object.hasOwn(SSO_ERROR_KEYS, code) ? SSO_ERROR_KEYS[code] : undefined
    error.value = t(key ?? 'login.ssoFailed')
  }
  // Drop the one-shot params so a refresh does not show the message again; the redirect stays.
  router.replace(redirectTarget ? { name: 'login', query: { redirect: redirectTarget } } : { name: 'login' })
})

function onSso(providerId: string) {
  window.location.href = `${apiBaseUrl}/auth/login/oidc/${encodeURIComponent(providerId)}?returnUrl=${encodeRedirectParam(redirectTarget ?? '/')}`
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

        <output v-if="notice" class="notice block text-muted-foreground">{{ notice }}</output>
        <p v-if="error" class="error" role="alert">{{ error }}</p>

        <template v-if="oidcProviders.length > 0">
          <div class="divider text-muted-foreground" role="separator" :aria-label="t('login.or')">{{ t('login.or') }}</div>
          <div class="grid gap-2">
            <Button
              v-for="provider in oidcProviders"
              :key="provider.id"
              type="button"
              variant="secondary"
              class="sso-btn w-full"
              data-test="sso"
              :data-provider="provider.id"
              @click="onSso(provider.id)"
            >
              <component :is="resolveSsoIcon(provider.icon)" class="size-[17px]" />
              <span>{{ t('login.ssoProvider', { name: provider.displayName }) }}</span>
            </Button>
          </div>
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
.notice {
  font-size: 0.85rem;
  margin: 0;
}
.error {
  color: var(--danger);
  font-size: 0.85rem;
  margin: 0;
}
</style>
