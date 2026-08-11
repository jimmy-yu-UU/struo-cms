<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Sun, Moon } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { useThemeStore } from '@/stores/themeStore'

const theme = useThemeStore()
const { t } = useI18n()
</script>

<template>
  <Button variant="ghost" size="icon" :aria-label="t('theme.toggle')" @click="theme.toggle()">
    <component :is="theme.isDark ? Sun : Moon" class="size-4" aria-hidden="true" />
  </Button>
</template>
