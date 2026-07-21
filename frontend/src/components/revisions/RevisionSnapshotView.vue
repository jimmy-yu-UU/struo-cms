<script setup lang="ts">
import { computed } from 'vue'
import Button from 'primevue/button'
import { useI18n } from 'vue-i18n'
import { revisionOperationKey } from '../../lib/revisionOperation'
import { formatRevisionTime } from '../../lib/formatRevisionTime'
import type { RevisionDetail } from '../../api/itemsApi'

const props = withDefaults(defineProps<{
  detail: RevisionDetail | null
  loading: boolean
  error: string
  canRevert: boolean
  reverting?: boolean
}>(), { reverting: false })
const emit = defineEmits<{ (e: 'revert', revisionNumber: number): void }>()
const { t } = useI18n()

const operationLabel = computed(() =>
  props.detail ? t(revisionOperationKey(props.detail.operation)) : '',
)
const whenText = computed(() =>
  props.detail ? formatRevisionTime(props.detail.createdAt) : '',
)
const whoText = computed(() => props.detail?.createdBy ?? t('revisions.system'))
const prettyJson = computed(() => {
  if (!props.detail) return ''
  try {
    return JSON.stringify(props.detail.snapshot, null, 2)
  } catch {
    return ''
  }
})
</script>

<template>
  <div class="rev-detail">
    <p v-if="loading" class="rev-notice">{{ t('revisions.loading') }}</p>
    <p v-else-if="error" class="rev-notice rev-error" role="alert">{{ error }}</p>
    <p v-else-if="!detail" class="rev-notice">{{ t('revisions.selectHint') }}</p>
    <template v-else>
      <header class="rev-summary">
        <div class="rev-summary__row">
          <span class="rev-badge">#{{ detail.revisionNumber }}</span>
          <span class="rev-op">{{ operationLabel }}</span>
        </div>
        <dl class="rev-meta">
          <div><dt>{{ t('revisions.colWhen') }}</dt><dd>{{ whenText }}</dd></div>
          <div><dt>{{ t('revisions.colWho') }}</dt><dd>{{ whoText }}</dd></div>
        </dl>
      </header>
      <section class="rev-snapshot">
        <h3>{{ t('revisions.snapshot') }}</h3>
        <pre class="rev-json">{{ prettyJson }}</pre>
      </section>
      <div v-if="canRevert" class="rev-actions">
        <Button
          class="rev-revert-btn"
          :label="t('revisions.revert')"
          icon="pi pi-replay"
          severity="warn"
          :loading="reverting"
          :disabled="reverting"
          @click="emit('revert', detail.revisionNumber)"
        />
      </div>
    </template>
  </div>
</template>

<style scoped>
.rev-detail { display: grid; gap: 16px; align-content: start; min-width: 0; }
.rev-notice { color: var(--muted); margin: 0; }
.rev-error { color: var(--danger); }
.rev-summary { display: grid; gap: 8px; }
.rev-summary__row { display: flex; align-items: center; gap: 10px; }
.rev-badge {
  font-variant-numeric: tabular-nums; font-weight: 700; color: var(--primary, var(--fg));
  background: color-mix(in srgb, var(--primary, #38bdf8) 12%, transparent);
  padding: 2px 8px; border-radius: var(--radius, 8px);
}
.rev-op { font-weight: 600; color: var(--fg); }
.rev-meta { display: grid; gap: 4px; margin: 0; }
.rev-meta div { display: flex; gap: 8px; font-size: .85rem; }
.rev-meta dt { color: var(--muted); margin: 0; min-width: 3.5rem; }
.rev-meta dd { color: var(--fg); margin: 0; }
.rev-snapshot h3 { margin: 0 0 6px; font-size: .8rem; color: var(--muted); font-weight: 500; }
.rev-json {
  margin: 0; padding: 12px; border: 1px solid var(--border); border-radius: var(--radius, 8px);
  background: var(--bg); color: var(--fg); font-size: .8rem; line-height: 1.5;
  max-height: 50vh; overflow: auto; white-space: pre; word-break: normal;
}
.rev-actions { display: flex; justify-content: flex-end; }
</style>
