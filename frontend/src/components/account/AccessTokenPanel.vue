<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useI18n } from 'vue-i18n'
import { usersApi, type AccessTokenInfo } from '../../api/usersApi'
import { formatDateTime } from '../../lib/formatDateTime'
import { createLatestWins } from '../../lib/latestWins'

// Read-only view of when a user's access token was issued and last used. The last-used time is
// written by the API at most once a minute, so the hint says it can trail the latest request.
const props = defineProps<{ userId: string }>()

const { t } = useI18n()

const info = ref<AccessTokenInfo | null>(null)
const loadFailed = ref(false)

const lastUsedText = computed(() =>
  info.value?.lastUsedAt ? formatDateTime(info.value.lastUsedAt) : t('accessToken.neverUsed'),
)

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

onMounted(reload)
defineExpose({ reload })
</script>

<template>
  <section class="access-token">
    <h2 class="panel-title">{{ t('accessToken.title') }}</h2>
    <p v-if="loadFailed" class="text-muted-foreground">{{ t('accessToken.loadFailed') }}</p>
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
  </section>
</template>

<style scoped>
.access-token { display: grid; gap: 8px; margin-top: 24px; }
.panel-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.times { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; margin: 0; }
.times dt { color: var(--muted-foreground); }
.times dd { margin: 0; font-variant-numeric: tabular-nums; }
</style>
