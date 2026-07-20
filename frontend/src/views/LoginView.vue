<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { storeToRefs } from 'pinia'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import { useAuthStore } from '../stores/authStore'
import { useAppConfigStore } from '../stores/appConfigStore'
import { apiBaseUrl } from '../api/apiClient'
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

async function onSubmit() {
  error.value = ''
  submitting.value = true
  try {
    await auth.login(email.value, password.value)
    router.push({ name: 'dashboard' })
  } catch (e) {
    error.value = e instanceof Error && e.message ? e.message : t('login.failed')
  } finally {
    submitting.value = false
  }
}

function onSso() {
  window.location.href = `${apiBaseUrl}/auth/login/oidc?returnUrl=/`
}
</script>

<template>
  <div class="login-wrap">
    <div class="login-card">
      <div class="brand">
        <BrandMark />
        <b>{{ brandName }}</b>
        <span class="caption">{{ t('login.subtitle') }}</span>
      </div>

      <form class="login-form" @submit.prevent="onSubmit">
        <div class="field">
          <label for="lg-email">{{ t('login.email') }}</label>
          <InputText
            id="lg-email"
            v-model="email"
            type="email"
            autocomplete="username"
            required
            autofocus
          />
        </div>

        <div class="field">
          <label for="lg-pw">{{ t('login.password') }}</label>
          <Password
            input-id="lg-pw"
            v-model="password"
            :feedback="false"
            toggle-mask
            :input-props="{ autocomplete: 'current-password', required: true }"
          />
        </div>

        <Button
          type="submit"
          class="btn-block"
          :loading="submitting"
          :label="t('login.submit')"
        />

        <p v-if="error" class="error" role="alert">{{ error }}</p>

        <template v-if="oidcEnabled">
          <div class="divider" role="separator" :aria-label="t('login.or')">{{ t('login.or') }}</div>
          <Button
            type="button"
            severity="secondary"
            class="btn-block sso-btn"
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
  border-radius: var(--radius-lg);
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
.brand .caption {
  font-size: 0.8rem;
  color: var(--muted);
}
.login-form {
  display: grid;
  gap: 18px;
}
.field {
  display: grid;
  gap: 6px;
}
.field label {
  font-size: 0.8rem;
  font-weight: 500;
  color: var(--muted);
}
.field :deep(.p-inputtext),
.field :deep(.p-password) {
  width: 100%;
}
.field :deep(.p-password input) {
  width: 100%;
}
.btn-block {
  width: 100%;
  justify-content: center;
}
.sso-btn {
  gap: 8px;
}
.divider {
  display: flex;
  align-items: center;
  gap: 14px;
  color: var(--muted);
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
