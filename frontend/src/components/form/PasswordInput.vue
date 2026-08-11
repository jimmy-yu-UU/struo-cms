<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Input } from '@/components/ui/input'
import { Button } from '@/components/ui/button'
import { Eye, EyeOff } from '@lucide/vue'

defineProps<{
  modelValue: string
  disabled?: boolean
  id?: string
  autocomplete?: string
}>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()
const { t } = useI18n()

const revealed = ref(false)
// The label names the NEXT action, so it must flip with the state — a toggle stuck on "Show
// password" is actively wrong once the value is visible.
const toggleLabel = computed(() => (revealed.value ? t('login.hidePassword') : t('login.showPassword')))
</script>

<template>
  <div class="relative">
    <Input
      :id="id"
      :model-value="modelValue"
      :type="revealed ? 'text' : 'password'"
      :disabled="disabled"
      :autocomplete="autocomplete"
      class="pr-10"
      @update:model-value="(v) => emit('update:modelValue', String(v ?? ''))"
    />
    <!--
      type="button" is load-bearing: a native <button> defaults to type="submit", and the intended
      consumer is a login <form> — an untyped toggle would submit that form instead of revealing
      the password.

      size="icon" contributes only a `size-*` utility, with no `has-[>svg]:px-*` of its own (that
      modifier-carrying padding lives on the plain/sm/lg sizes only), so the `px-2.5` below competes
      with nothing and applies as written. Switching this trigger to a size that DOES carry
      `has-[>svg]:px-*` would need `px-2.5` re-supplied under that same modifier, or CSS specificity
      would let the vendored padding win silently.
    -->
    <Button
      type="button"
      variant="ghost"
      size="icon"
      :disabled="disabled"
      :aria-label="toggleLabel"
      :aria-pressed="revealed"
      class="absolute right-0 top-0 h-full px-2.5 hover:bg-transparent"
      @click="revealed = !revealed"
    >
      <component :is="revealed ? EyeOff : Eye" class="size-4" />
    </Button>
  </div>
</template>
