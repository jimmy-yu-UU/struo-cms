<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Select from 'primevue/select'
import { useUiLocaleStore } from '../../stores/uiLocaleStore'
import type { UiLocale } from '../../theme/resolveInitialUiLocale'

const uiLocale = useUiLocaleStore()
const { t } = useI18n()

const options = computed(() => [
  { value: 'zh-TW', label: t('lang.zh-TW') },
  { value: 'en', label: t('lang.en') },
])

function onChange(value: UiLocale) {
  uiLocale.set(value)
}
</script>

<template>
  <Select
    :model-value="uiLocale.locale"
    :options="options"
    option-label="label"
    option-value="value"
    :aria-label="t('lang.label')"
    class="lang-switcher"
    @update:model-value="onChange"
  />
</template>
