<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'

// The "Password" section of the user edit page: one short line saying what a reset does, and the reset
// action. The page owns the dialog and what happens after it; this only raises `reset`. A disabled
// button gets no pointer events, so the reason is also the tooltip of the span around it.
defineProps<{ disabled?: boolean; disabledHint?: string }>()
const emit = defineEmits<{ (e: 'reset'): void }>()
const { t } = useI18n()
</script>

<template>
  <section class="password-panel">
    <h2 class="panel-title">{{ t('password.sectionTitle') }}</h2>
    <p class="text-muted-foreground text-[0.9rem]">{{ t('password.sectionDescription') }}</p>
    <div class="actions">
      <span class="inline-flex" :title="disabled ? disabledHint : undefined">
        <!-- type="button" is defensive: ui/button injects no type of its own, so a control re-parented
             inside a <form> would otherwise start submitting it. -->
        <Button type="button" variant="outline" :disabled="disabled" :title="disabled ? disabledHint : undefined"
                @click="emit('reset')">
          {{ t('password.resetTitle') }}
        </Button>
      </span>
    </div>
  </section>
</template>

<style scoped>
.password-panel { display: grid; gap: 12px; }
.panel-title { font-size: 1.05rem; font-weight: 600; margin: 0; }
.actions { display: flex; gap: 8px; flex-wrap: wrap; }
</style>
