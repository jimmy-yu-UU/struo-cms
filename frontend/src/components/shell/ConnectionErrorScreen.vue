<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Button } from '@/components/ui/button'

const props = withDefaults(defineProps<{ busy?: boolean }>(), { busy: false })
defineEmits<{ retry: [] }>()
const { t } = useI18n()

// Focus lands on the heading when the screen appears and again when a retry fails, so a keyboard
// or screen-reader user is told where they are instead of being left on a disabled button.
const heading = ref<HTMLElement | null>(null)
onMounted(() => heading.value?.focus())
watch(() => props.busy, (busy, wasBusy) => {
  if (wasBusy && !busy) heading.value?.focus()
}, { flush: 'post' })
</script>

<template>
  <main class="flex min-h-screen flex-col items-center justify-center gap-4 p-6 text-center">
    <h1 ref="heading" tabindex="-1" class="text-xl font-semibold outline-none">{{ t('errors.connectionTitle') }}</h1>
    <p class="text-muted-foreground" role="alert">{{ t('errors.connectionBody') }}</p>
    <Button :disabled="busy" :aria-busy="busy" @click="$emit('retry')">
      {{ busy ? t('errors.connectionRetrying') : t('common.retry') }}
    </Button>
  </main>
</template>
