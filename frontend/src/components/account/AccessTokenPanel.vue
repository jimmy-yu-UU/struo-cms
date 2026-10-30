<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { useConfirm } from '@/composables/useConfirm'
import { useToast } from '@/composables/useToast'
import { usersApi, type AccessTokenInfo } from '../../api/usersApi'
import { formatDateTime } from '../../lib/formatDateTime'
import { createLatestWins } from '../../lib/latestWins'

// Shows when a user's access token was issued and last used, and lets a super-admin generate,
// regenerate or revoke it. The last-used time is written by the API at most once a minute, so the
// hint says it can trail the latest request. A freshly issued token is held in `issuedToken` only
// while its dialog is open: the server keeps just the hash, and dismissing the dialog clears it.
const props = defineProps<{ userId: string }>()

const { t } = useI18n()
const confirm = useConfirm()
const toast = useToast()

const info = ref<AccessTokenInfo | null>(null)
const loadFailed = ref(false)

const lastUsedText = computed(() =>
  info.value?.lastUsedAt ? formatDateTime(info.value.lastUsedAt) : t('accessToken.neverUsed'),
)

const issuedToken = ref<string | null>(null)
const busy = ref(false)

const reloadGuard = createLatestWins()
async function reload(): Promise<void> {
  const token = reloadGuard.next()
  loadFailed.value = false
  try {
    const next = await usersApi.getAccessTokenInfo(props.userId)
    if (!reloadGuard.isCurrent(token)) return
    info.value = next
  } catch {
    if (!reloadGuard.isCurrent(token)) return
    loadFailed.value = true
    info.value = null
  }
}

async function onGenerate(): Promise<void> {
  if (busy.value) return
  // Issuing a token overwrites the current hash, so the old token dies with this call.
  if (info.value?.hasToken) {
    const accepted = await confirm.require({
      header: t('accessToken.regenerateConfirmHeader'),
      message: t('accessToken.regenerateConfirmMessage'),
      severity: 'danger',
    })
    if (!accepted) return
  }
  busy.value = true
  try {
    const { token } = await usersApi.generateAccessToken(props.userId)
    issuedToken.value = token
    await reload()
  } catch {
    toast.add({ severity: 'error', summary: t('accessToken.generateFailed'), life: 3500 })
  } finally {
    busy.value = false
  }
}

async function onRevoke(): Promise<void> {
  if (busy.value) return
  const accepted = await confirm.require({
    header: t('accessToken.revokeConfirmHeader'),
    message: t('accessToken.revokeConfirmMessage'),
    severity: 'danger',
  })
  if (!accepted) return
  busy.value = true
  try {
    await usersApi.revokeAccessToken(props.userId)
    toast.add({ severity: 'success', summary: t('accessToken.revoked'), life: 2000 })
    await reload()
  } catch {
    toast.add({ severity: 'error', summary: t('accessToken.revokeFailed'), life: 3500 })
  } finally {
    busy.value = false
  }
}

async function onCopy(): Promise<void> {
  if (issuedToken.value === null) return
  try {
    await navigator.clipboard.writeText(issuedToken.value)
    toast.add({ severity: 'success', summary: t('accessToken.copied'), life: 2000 })
  } catch {
    // Never silent: this is the only chance to hand the value over.
    toast.add({ severity: 'error', summary: t('accessToken.copyFailed'), life: 3500 })
  }
}

function dismissToken(): void {
  issuedToken.value = null
}

onMounted(reload)
defineExpose({ reload })
</script>

<template>
  <section class="access-token">
    <h2 class="panel-title">{{ t('accessToken.title') }}</h2>
    <p v-if="!info && !loadFailed" class="text-muted-foreground">{{ t('common.loading') }}</p>
    <p v-else-if="loadFailed" class="text-muted-foreground">{{ t('accessToken.loadFailed') }}</p>
    <p v-else-if="info && !info.hasToken" class="text-muted-foreground">{{ t('accessToken.none') }}</p>
    <template v-else-if="info">
      <dl class="times">
        <dt>{{ t('accessToken.createdAt') }}</dt>
        <dd>{{ formatDateTime(info.createdAt) }}</dd>
        <dt>{{ t('accessToken.lastUsedAt') }}</dt>
        <dd>{{ lastUsedText }}</dd>
      </dl>
      <p class="text-muted-foreground text-[0.9rem]">{{ t('accessToken.hint') }}</p>
    </template>

    <div v-if="info" class="actions">
      <Button type="button" variant="outline" :disabled="busy" @click="onGenerate">
        {{ info.hasToken ? t('accessToken.regenerate') : t('accessToken.generate') }}
      </Button>
      <Button v-if="info.hasToken" type="button" variant="outline" :disabled="busy" @click="onRevoke">
        {{ t('accessToken.revoke') }}
      </Button>
    </div>

    <Dialog :open="issuedToken !== null" @update:open="(v: boolean) => { if (!v) dismissToken() }">
      <!-- An outside click is not a deliberate dismissal: the token is shown once, so only the Close
           button, the X and Esc discard it. -->
      <DialogScrollContent @interact-outside="(e: Event) => e.preventDefault()">
        <DialogHeader>
          <DialogTitle>{{ t('accessToken.issuedTitle') }}</DialogTitle>
          <DialogDescription>{{ t('accessToken.issuedDescription') }}</DialogDescription>
        </DialogHeader>
        <code class="issued-token">{{ issuedToken }}</code>
        <p class="issued-note">{{ t('accessToken.issuedNote') }}</p>
        <DialogFooter>
          <Button type="button" variant="outline" @click="onCopy">{{ t('accessToken.copy') }}</Button>
          <Button type="button" @click="dismissToken">{{ t('common.close') }}</Button>
        </DialogFooter>
      </DialogScrollContent>
    </Dialog>
  </section>
</template>

<style scoped>
.access-token { display: grid; gap: 8px; margin-top: 24px; }
.panel-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.times { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 0; }
.times dt { color: var(--muted-foreground); }
.actions { display: flex; gap: 8px; flex-wrap: wrap; }
.issued-token { display: block; padding: 8px 12px; border: 1px solid var(--border); border-radius: 6px; word-break: break-all; user-select: all; }
.issued-note { margin: 0; font-size: .85rem; color: var(--danger); }
.times dd { margin: 0; font-variant-numeric: tabular-nums; }
</style>
