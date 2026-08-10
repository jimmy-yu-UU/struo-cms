<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import type { QuickAction } from '../../lib/quickActions'

defineProps<{ actions: QuickAction[] }>()
defineEmits<{ (e: 'run', action: QuickAction): void }>()
const { t } = useI18n()

function labelFor(action: QuickAction): string {
  return action.kind === 'uploadMedia'
    ? t('dashboard.quick.uploadMedia')
    : t('dashboard.quick.newItem', { label: action.label })
}

function iconFor(action: QuickAction): string {
  return action.kind === 'uploadMedia' ? 'pi pi-upload' : 'pi pi-plus'
}
</script>

<template>
  <div class="quick">
    <p v-if="actions.length === 0" class="quick__empty">{{ t('dashboard.quick.empty') }}</p>
    <Button
      v-for="(action, i) in actions"
      :key="i"
      data-test="quick-action"
      class="quick__btn"
      severity="secondary"
      :label="labelFor(action)"
      :icon="iconFor(action)"
      @click="$emit('run', action)"
    />
  </div>
</template>

<style scoped>
.quick {
  display: grid;
  gap: 10px;
}
.quick__btn {
  width: 100%;
  justify-content: flex-start;
}
.quick__empty {
  color: var(--legacy-muted);
}
</style>
