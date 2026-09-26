<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { useUiLocaleStore } from '@/stores/uiLocaleStore'
import type { UiLocale } from '@/locales'

const uiLocale = useUiLocaleStore()
const { t } = useI18n()

const options = computed(() => uiLocale.enabled.map((value) => ({ value, label: t(`lang.${value}`) })))

function onChange(value: unknown): void {
  uiLocale.set(value as UiLocale)
}
</script>

<template>
  <!-- One enabled locale means nothing to switch; the control is omitted entirely.
       Hidden on the narrowest viewports via a Tailwind utility on the trigger itself, not a
       specificity fight in theme.css. -->
  <Select v-if="options.length > 1" :model-value="uiLocale.locale" @update:model-value="onChange">
    <SelectTrigger class="w-36 max-[520px]:hidden" :aria-label="t('lang.label')">
      <SelectValue />
    </SelectTrigger>
    <SelectContent>
      <SelectItem v-for="o in options" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
    </SelectContent>
  </Select>
</template>
