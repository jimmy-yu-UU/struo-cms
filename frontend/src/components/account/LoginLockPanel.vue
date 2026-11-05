<script setup lang="ts">
import { ref, computed, watch, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'
import { useToast } from '@/composables/useToast'
import { loginLocksApi, type LoginLockState } from '../../api/loginLocksApi'
import { ApiError } from '../../api/apiClient'
import { createLatestWins } from '../../lib/latestWins'

// Shows whether password sign-in for one email is locked and lets the holder of login.unlock (or a
// super-admin) clear the lock. The parent decides who may see it; the API enforces the permission.
// The lock is keyed on the address alone, so an email with no account reads back as "not locked".
const props = withDefaults(defineProps<{ email: string; heading?: boolean }>(), { heading: false })

const { t } = useI18n()
const toast = useToast()

const state = ref<LoginLockState | null>(null)
const errorKey = ref<string | null>(null)
const busy = ref(false)
const statusEl = ref<HTMLElement | null>(null)

const guard = createLatestWins()

// A 403 or 400 on the lookup reads the same; onUnlock maps its own 403 to the super-admin message.
function errorKeyFor(e: unknown, otherwise: string): string {
  if (e instanceof ApiError && e.status === 403) return 'loginLocks.notPermitted'
  if (e instanceof ApiError && e.status === 400) return 'loginLocks.badEmail'
  return otherwise
}

async function reload(): Promise<void> {
  const token = guard.next()
  errorKey.value = null
  if (props.email === '') {
    state.value = null
    return
  }
  try {
    const next = await loginLocksApi.get(props.email)
    if (!guard.isCurrent(token)) return
    state.value = next
  } catch (e) {
    if (!guard.isCurrent(token)) return
    state.value = null
    errorKey.value = errorKeyFor(e, 'loginLocks.loadFailed')
  }
}

async function onUnlock(): Promise<void> {
  if (busy.value) return
  busy.value = true
  try {
    await loginLocksApi.clear(props.email)
    toast.add({ severity: 'success', summary: t('loginLocks.unlocked'), life: 3000 })
    await reload()
    // The Unlock button the user just pressed is gone now; keep focus on the status text instead of
    // letting it fall back to the page body.
    await nextTick()
    statusEl.value?.focus()
  } catch (e) {
    // The page is open only to a holder, so a 403 here means the account is a super-admin's.
    const key = e instanceof ApiError && e.status === 403
      ? 'loginLocks.superAdminOnly'
      : errorKeyFor(e, 'loginLocks.unlockFailed')
    toast.add({ severity: 'error', summary: t(key), life: 3500 })
  } finally {
    busy.value = false
  }
}

// Whole seconds under a minute, whole minutes (rounded up) from a minute on.
const remaining = computed(() => {
  const seconds = state.value?.retryAfterSeconds ?? 0
  return seconds < 60
    ? t('loginLocks.durationSeconds', { n: seconds })
    : t('loginLocks.durationMinutes', { n: Math.ceil(seconds / 60) })
})

watch(() => props.email, reload, { immediate: true })
defineExpose({ reload })
</script>

<template>
  <section class="login-lock">
    <h2 v-if="heading" class="panel-title">{{ t('loginLocks.title') }}</h2>
    <p v-if="errorKey" class="text-muted-foreground" role="alert">{{ t(errorKey) }}</p>
    <!-- Always rendered, so a screen reader registers the live region before its text changes. -->
    <div ref="statusEl" role="status" aria-live="polite" tabindex="-1" class="status">
      <template v-if="state && !errorKey">
        <p v-if="state.locked">{{ t('loginLocks.locked', { time: remaining }) }}</p>
        <p v-else class="text-muted-foreground">{{ t('loginLocks.notLocked') }}</p>
      </template>
    </div>
    <div v-if="state?.locked && !errorKey" class="actions">
      <Button type="button" variant="outline" :disabled="busy" @click="onUnlock">
        {{ busy ? t('loginLocks.unlocking') : t('loginLocks.unlock') }}
      </Button>
    </div>
  </section>
</template>

<style scoped>
.login-lock { display: grid; gap: 8px; }
.panel-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.status { outline: none; }
.actions { display: flex; gap: 8px; flex-wrap: wrap; }
</style>
