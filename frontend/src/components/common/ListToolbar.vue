<!-- frontend/src/components/common/ListToolbar.vue -->
<script setup lang="ts">
import InputText from 'primevue/inputtext'

defineProps<{ searchValue: string; searchPlaceholder?: string }>()
const emit = defineEmits<{ search: [value: string] }>()

function onInput(e: Event): void {
  emit('search', (e.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="toolbar">
    <span class="iwrap">
      <i class="pi pi-search" aria-hidden="true" />
      <InputText
        type="search"
        :model-value="searchValue"
        :placeholder="searchPlaceholder"
        :aria-label="searchPlaceholder"
        @input="onInput"
      />
    </span>
    <div class="filters">
      <slot name="filters" />
    </div>
  </div>
</template>

<style scoped>
.toolbar {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
  padding: 0 0 14px;
}
.iwrap {
  position: relative;
  flex: 1 1 220px;
  max-width: 320px;
  display: flex;
  align-items: center;
}
.iwrap .pi-search {
  position: absolute;
  left: 12px;
  color: var(--muted);
  pointer-events: none;
}
.iwrap :deep(input) {
  width: 100%;
  padding-left: 34px;
}
.filters { display: flex; gap: 10px; align-items: center; margin-left: auto; }
</style>
