<script setup lang="ts">
import { computed } from 'vue'
import { RotateCcw } from '@lucide/vue'
import { Button } from '@/components/ui/button'
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
const revertLabel = computed(() => (props.reverting ? t('revisions.reverting') : t('revisions.revert')))
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
    <p v-if="loading" class="rev-notice text-muted-foreground">{{ t('revisions.loading') }}</p>
    <p v-else-if="error" class="rev-notice rev-error text-muted-foreground" role="alert">{{ error }}</p>
    <p v-else-if="!detail" class="rev-notice text-muted-foreground">{{ t('revisions.selectHint') }}</p>
    <template v-else>
      <header class="rev-summary">
        <div class="rev-summary__row">
          <span class="rev-badge tabular-nums font-bold text-primary bg-primary/10 px-2 py-0.5 rounded-md">#{{ detail.revisionNumber }}</span>
          <span class="rev-op">{{ operationLabel }}</span>
        </div>
        <dl class="rev-meta">
          <div><dt class="text-muted-foreground">{{ t('revisions.colWhen') }}</dt><dd>{{ whenText }}</dd></div>
          <div><dt class="text-muted-foreground">{{ t('revisions.colWho') }}</dt><dd>{{ whoText }}</dd></div>
        </dl>
      </header>
      <section class="rev-snapshot">
        <h3 class="text-muted-foreground">{{ t('revisions.snapshot') }}</h3>
        <pre class="rev-json rounded-md">{{ prettyJson }}</pre>
      </section>
      <div v-if="canRevert" class="rev-actions">
        <!-- Outline plus the warning border matches the warning-banner idiom already used on the
             collection list, which keeps the default foreground colour rather than tinting the text. -->
        <Button
          type="button"
          variant="outline"
          class="rev-revert-btn border-warning hover:bg-warning/10"
          :disabled="reverting"
          @click="emit('revert', detail.revisionNumber)"
        >
          <RotateCcw aria-hidden="true" />
          {{ revertLabel }}
        </Button>
      </div>
    </template>
  </div>
</template>

<style scoped>
.rev-detail { display: grid; gap: 16px; align-content: start; min-width: 0; }
.rev-notice { margin: 0; }
.rev-error { color: var(--danger); }
.rev-summary { display: grid; gap: 8px; }
.rev-summary__row { display: flex; align-items: center; gap: 10px; }
.rev-op { font-weight: 600; color: var(--fg); }
.rev-meta { display: grid; gap: 4px; margin: 0; }
.rev-meta div { display: flex; gap: 8px; font-size: .85rem; }
.rev-meta dt { margin: 0; min-width: 3.5rem; }
.rev-meta dd { color: var(--fg); margin: 0; }
.rev-snapshot h3 { margin: 0 0 6px; font-size: .8rem; font-weight: 500; }
.rev-json {
  margin: 0; padding: 12px; border: 1px solid var(--border);
  background: var(--surface-2); color: var(--fg); font-size: .8rem; line-height: 1.5;
  max-height: 50vh; overflow: auto; white-space: pre; word-break: normal;
}
.rev-actions { display: flex; justify-content: flex-end; }
</style>
