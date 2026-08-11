<script setup lang="ts">
import { ref } from 'vue'
import { Baseline } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover'

defineOptions({ name: 'RichTextColorMenu' })

defineProps<{ disabled?: boolean; activeColor?: string | null }>()
const emit = defineEmits<{ (e: 'pick', color: string): void; (e: 'clear'): void }>()

const open = ref(false)

const PALETTE = [
  '#0f172a', '#64748b', '#dc2626', '#ea580c', '#ca8a04',
  '#16a34a', '#0891b2', '#2563eb', '#7c3aed', '#db2777',
] as const

function pick(color: string): void {
  emit('pick', color)
  open.value = false
}

function onFreePick(e: Event): void {
  emit('pick', (e.target as HTMLInputElement).value)
}

function clear(): void {
  emit('clear')
  open.value = false
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <!-- type="button" is explicit even though PopoverTrigger (as-child) already merges its own
           type="button" onto whatever it wraps: this sits inside ItemForm.vue's <form>, so the
           Button doesn't rely on the merge behaviour of the component wrapping it. -->
      <Button type="button" variant="ghost" size="icon" data-cmd="color" :disabled="disabled" aria-label="Text colour" title="Text colour">
        <Baseline :style="activeColor ? { color: activeColor } : undefined" />
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-auto p-3">
      <div class="grid grid-cols-5 gap-1.5">
        <button v-for="c in PALETTE" :key="c" type="button" class="size-[22px] rounded border" :data-color="c"
          :style="{ background: c }" :aria-label="`Colour ${c}`" @click="pick(c)" />
      </div>
      <label class="mt-2 flex items-center gap-1.5 text-sm cursor-pointer">
        <input type="color" data-cmd="colorFree" class="h-6 w-8 cursor-pointer" @change="onFreePick">
        Custom…
      </label>
      <Button type="button" variant="outline" size="sm" data-cmd="colorClear" class="mt-2 w-full" @click="clear">Clear colour</Button>
    </PopoverContent>
  </Popover>
</template>
