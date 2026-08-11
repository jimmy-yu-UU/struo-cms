<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useUiLocaleStore } from '@/stores/uiLocaleStore'
import type { UiLocale } from '@/theme/resolveInitialUiLocale'

const uiLocale = useUiLocaleStore()
const { t } = useI18n()

const options = computed(() => [
  { value: 'zh-TW' as const, label: t('lang.zh-TW') },
  { value: 'en' as const, label: t('lang.en') },
])

function onChange(value: unknown): void {
  uiLocale.set(value as UiLocale)
}
</script>

<template>
  <!-- Hidden on the narrowest viewports via a Tailwind utility on the trigger itself, not a
       specificity fight in theme.css. -->
  <Select :model-value="uiLocale.locale" @update:model-value="onChange">
    <SelectTrigger class="w-36 max-[520px]:hidden" :aria-label="t('lang.label')">
      <SelectValue />
    </SelectTrigger>
    <SelectContent>
      <SelectItem v-for="o in options" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
    </SelectContent>
  </Select>
</template>
