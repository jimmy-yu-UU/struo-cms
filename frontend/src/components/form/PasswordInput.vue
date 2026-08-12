<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Eye, EyeOff } from '@lucide/vue'

defineProps<{
  modelValue: string
  disabled?: boolean
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()
// Everything else a consumer passes (id, autocomplete, required, name, placeholder, aria-*, …)
// is forwarded verbatim to the native input via $attrs below, so it needs no dedicated prop here.
defineOptions({ inheritAttrs: false })
const { t } = useI18n()

const revealed = ref(false)
// The label names the NEXT action, so it must flip with the state — a toggle stuck on "Show
// password" is actively wrong once the value is visible. This is the only state signal the toggle
// gives; it deliberately does not also carry aria-pressed, which would announce the same state
// twice.
const toggleLabel = computed(() => (revealed.value ? t('login.hidePassword') : t('login.showPassword')))
</script>

<template>
  <div class="relative">
    <Input
      v-bind="$attrs"
      :model-value="modelValue"
      :type="revealed ? 'text' : 'password'"
      :disabled="disabled"
      class="pr-10"
      @update:model-value="(v) => emit('update:modelValue', String(v ?? ''))"
    />
    <!--
      type="button" is load-bearing: a native <button> defaults to type="submit", and the intended
      consumer is a login <form> — an untyped toggle would submit that form instead of revealing
      the password.

      size="icon" carries no has-[>svg]:px-* (unlike default/xs/sm/lg), so px-2.5 below competes
      with nothing and applies as written. dark:hover:bg-transparent re-supplies the same modifier
      as ghost's own dark:hover:bg-accent/50 — without it, hover:bg-transparent only wins in light
      mode and this toggle keeps a hover background in dark mode.
    -->
    <Button
      type="button"
      variant="ghost"
      size="icon"
      :disabled="disabled"
      :aria-label="toggleLabel"
      class="absolute right-0 top-0 h-full px-2.5 hover:bg-transparent dark:hover:bg-transparent"
      @click="revealed = !revealed"
    >
      <component :is="revealed ? EyeOff : Eye" />
    </Button>
  </div>
</template>
