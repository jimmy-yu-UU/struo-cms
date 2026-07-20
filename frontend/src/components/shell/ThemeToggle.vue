<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useThemeStore } from '../../stores/themeStore'

const theme = useThemeStore()
const { t } = useI18n()
</script>

<template>
  <button
    type="button"
    class="icon-btn theme-toggle"
    :aria-label="t('theme.toggle')"
    @click="theme.toggle()"
  >
    <i class="pi" :class="theme.isDark ? 'pi-sun' : 'pi-moon'" aria-hidden="true" />
  </button>
</template>
