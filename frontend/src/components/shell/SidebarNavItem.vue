<script setup lang="ts">
const props = withDefaults(
  defineProps<{ label: string; icon?: string | null; active?: boolean }>(),
  { icon: null, active: false },
)
defineEmits<{ activate: [] }>()
</script>

<template>
  <button
    type="button"
    class="nav-item"
    :class="{ active: props.active }"
    @click="$emit('activate')"
  >
    <i class="nav-icon" :class="props.icon || 'pi pi-file'" aria-hidden="true" />
    <span class="nav-label">{{ props.label }}</span>
  </button>
</template>
