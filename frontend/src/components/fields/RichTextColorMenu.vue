<script setup lang="ts">
import { ref } from 'vue'

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
  <div class="color-menu">
    <button type="button" data-cmd="color" :disabled="disabled" aria-label="Text colour" title="Text colour"
      class="color-menu__trigger" @click="open = !open">
      <span :style="activeColor ? { color: activeColor } : undefined">A</span>
    </button>
    <div v-if="open" class="color-menu__panel">
      <div class="color-menu__swatches">
        <button v-for="c in PALETTE" :key="c" type="button" class="color-menu__swatch" :data-color="c"
          :style="{ background: c }" :aria-label="`Colour ${c}`" @click="pick(c)" />
      </div>
      <label class="color-menu__free">
        <input type="color" data-cmd="colorFree" @change="onFreePick" />
        Custom…
      </label>
      <button type="button" data-cmd="colorClear" class="color-menu__clear" @click="clear">Clear colour</button>
    </div>
  </div>
</template>

<style scoped>
.color-menu { position: relative; display: inline-block; }
.color-menu__trigger { min-width: 30px; padding: 2px 6px; cursor: pointer; background: transparent; border: 1px solid transparent; border-radius: 4px; font-weight: 700; }
.color-menu__trigger:disabled { opacity: 0.5; cursor: not-allowed; }
.color-menu__panel { position: absolute; z-index: 10; top: 100%; left: 0; margin-top: 4px; padding: 8px; background: var(--surface-0, #fff); border: 1px solid var(--surface-border, #d0d0d0); border-radius: 6px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15); width: max-content; }
.color-menu__swatches { display: grid; grid-template-columns: repeat(5, 22px); gap: 6px; }
.color-menu__swatch { width: 22px; height: 22px; border: 1px solid var(--surface-border, #d0d0d0); border-radius: 4px; cursor: pointer; }
.color-menu__free { display: flex; align-items: center; gap: 6px; margin-top: 8px; font-size: 0.85rem; cursor: pointer; }
.color-menu__clear { display: block; margin-top: 8px; width: 100%; padding: 2px 6px; cursor: pointer; background: transparent; border: 1px solid var(--surface-border, #d0d0d0); border-radius: 4px; }
</style>
