<script setup lang="ts">
import { ref } from 'vue'
import { IN_TABLE_ACTIONS, type TableAction } from './richTextTableActions'

defineOptions({ name: 'RichTextTableMenu' })

defineProps<{ disabled?: boolean; inTable: boolean }>()
const emit = defineEmits<{ (e: 'action', action: TableAction): void }>()

const open = ref(false)
const inTableActions = IN_TABLE_ACTIONS

function run(action: TableAction): void {
  emit('action', action)
  open.value = false
}
</script>

<template>
  <div class="table-menu">
    <button type="button" data-cmd="table" :disabled="disabled" aria-label="Table" title="Table"
      class="table-menu__trigger" @click="open = !open"><i class="pi pi-table" /></button>
    <div v-if="open" class="table-menu__panel">
      <button type="button" data-cmd="tableInsert" class="table-menu__item" @click="run('insert')">
        Insert 3×3 table
      </button>
      <button v-for="[action, label] in inTableActions" :key="action" type="button"
        :data-cmd="`table-${action}`" class="table-menu__item" :disabled="!inTable"
        @click="run(action)">{{ label }}</button>
    </div>
  </div>
</template>

<style scoped>
.table-menu { position: relative; display: inline-block; }
.table-menu__trigger { min-width: 30px; padding: 2px 6px; cursor: pointer; background: transparent; border: 1px solid transparent; border-radius: 4px; }
.table-menu__trigger:disabled { opacity: 0.5; cursor: not-allowed; }
.table-menu__panel { position: absolute; z-index: 10; top: 100%; left: 0; margin-top: 4px; padding: 4px; background: var(--surface); border: 1px solid var(--border); border-radius: 6px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15); width: max-content; display: flex; flex-direction: column; }
.table-menu__item { text-align: left; padding: 4px 10px; cursor: pointer; background: transparent; border: none; border-radius: 4px; }
.table-menu__item:hover:not(:disabled) { background: var(--bg); }
.table-menu__item:disabled { opacity: 0.5; cursor: not-allowed; }
</style>
