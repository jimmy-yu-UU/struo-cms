<!-- frontend/src/components/common/ListToolbar.vue -->
<script setup lang="ts">
import { Search } from '@lucide/vue'
import { Input } from '@/components/ui/input'

defineProps<{ searchValue: string; searchPlaceholder?: string }>()
const emit = defineEmits<{ search: [value: string] }>()

// A native @input, not @update:model-value: the parent debounces and owns the value, and reading
// the DOM event keeps the emitted payload a plain string regardless of the vendored control's
// own string | number model type.
function onInput(e: Event): void {
  emit('search', (e.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="toolbar flex flex-wrap items-center gap-2.5 pb-3.5">
    <!-- grow/shrink/basis rather than flex-1: flex-1 is `flex: 1 1 0%`, which would discard the
         220px flex-basis this search box has always had and let it collapse to nothing next to a
         wide filter row. -->
    <span class="relative flex max-w-80 shrink grow basis-[220px] items-center">
      <Search
        class="pointer-events-none absolute left-3 size-4 text-muted-foreground"
        aria-hidden="true"
      />
      <!-- pl-9 clears the absolutely-positioned icon. The vendored Input is already w-full, so
           the old `.iwrap :deep(input) { width: 100% }` rule this replaces is gone, not ported. -->
      <Input
        type="search"
        :model-value="searchValue"
        :placeholder="searchPlaceholder"
        :aria-label="searchPlaceholder"
        class="pl-9"
        @input="onInput"
      />
    </span>
    <div class="filters ml-auto flex items-center gap-2.5">
      <slot name="filters" />
    </div>
  </div>
</template>
