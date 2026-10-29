<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'

defineEmits<{ retry: [] }>()
const { t } = useI18n()
</script>

<template>
  <main class="flex min-h-screen flex-col items-center justify-center gap-4 p-6 text-center">
    <h1 class="text-xl font-semibold">{{ t('errors.connectionTitle') }}</h1>
    <p class="text-muted-foreground">{{ t('errors.connectionBody') }}</p>
    <Button @click="$emit('retry')">{{ t('common.retry') }}</Button>
  </main>
</template>
