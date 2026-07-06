<script setup lang="ts">
import { computed } from 'vue'
import FilePicker from './FilePicker.vue'
import type { FieldMeta } from '../../types/schema'
const props = defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
const isImage = computed(() => props.field.interface === 'image')
</script>
<template>
  <FilePicker :model-value="(modelValue as string | null)" :image="isImage" :disabled="disabled"
    @update:model-value="(v: string | null) => $emit('update:modelValue', v)" />
</template>
