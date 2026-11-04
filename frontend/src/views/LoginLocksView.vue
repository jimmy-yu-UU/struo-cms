<script setup lang="ts">
import { ref, computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import PageHeader from '../components/common/PageHeader.vue'
import LoginLockPanel from '../components/account/LoginLockPanel.vue'
import { useAuthStore } from '../stores/authStore'
import { LOGIN_UNLOCK } from '../lib/capabilities'

// Look up one email's login lock and clear it. Reserved for a super-admin or a holder of login.unlock;
// the sidebar hides the entry otherwise and the API answers 403 regardless.
const { t } = useI18n()
const auth = useAuthStore()

const allowed = computed(() => auth.hasCapability(LOGIN_UNLOCK))

// The API trims and lowercases the address itself, so the field is only trimmed here.
const emailInput = ref('')
const lookedUp = ref('')
// Bumped on every submit so the same address is read again instead of being treated as unchanged.
const lookupRound = ref(0)

const canSubmit = computed(() => emailInput.value.trim() !== '')

function onSubmit(): void {
  if (!canSubmit.value) return
  lookedUp.value = emailInput.value.trim()
  lookupRound.value += 1
}
</script>

<template>
  <div v-if="!allowed" class="text-muted-foreground p-6" role="alert">{{ t('loginLocks.notPermitted') }}</div>
  <template v-else>
    <PageHeader :title="t('loginLocks.title')" :caption="t('loginLocks.description')" />
    <section class="lock-lookup">
      <!-- novalidate: an account may hold any non-blank email, so the browser's address check must not
           stop a lookup. -->
      <form class="lookup-form" novalidate @submit.prevent="onSubmit">
        <label class="lookup-field" for="login-lock-email">
          <span>{{ t('loginLocks.emailLabel') }}</span>
          <Input id="login-lock-email" v-model="emailInput" type="email" maxlength="320" autocomplete="off" />
        </label>
        <Button type="submit" :disabled="!canSubmit">{{ t('loginLocks.lookup') }}</Button>
      </form>
      <LoginLockPanel v-if="lookedUp" :key="lookupRound" :email="lookedUp" />
    </section>
  </template>
</template>

<style scoped>
.lock-lookup { max-width: 640px; display: grid; gap: 20px; }
.lookup-form { display: flex; align-items: flex-end; gap: 12px; flex-wrap: wrap; }
.lookup-field { display: flex; flex: 1; flex-direction: column; gap: 8px; min-width: 240px; }
</style>
