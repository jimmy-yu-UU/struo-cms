<script setup lang="ts">
import { ref } from 'vue'
import { filesApi, type FileMeta } from '../../api/filesApi'

const props = defineProps<{ folderId?: string | null }>()
const emit = defineEmits<{ (e: 'uploaded', meta: FileMeta): void; (e: 'done'): void }>()

type Row = { name: string; state: 'uploading' | 'error'; error?: string }
const rows = ref<Row[]>([])
const dragging = ref(false)

async function uploadFiles(files: File[]): Promise<void> {
  await Promise.all(
    files.map(async (file) => {
      const row: Row = { name: file.name, state: 'uploading' }
      rows.value = [...rows.value, row]
      try {
        const meta = await filesApi.upload(file, props.folderId)
        rows.value = rows.value.filter((r) => r !== row)
        emit('uploaded', meta)
      } catch (e) {
        row.state = 'error'
        row.error = e instanceof Error ? e.message : 'Upload failed.'
        rows.value = [...rows.value] // trigger reactivity
      }
    }),
  )
  emit('done')
}

function onInput(e: Event): void {
  const input = e.target as HTMLInputElement
  if (input.files) void uploadFiles(Array.from(input.files))
  input.value = ''
}

function onDrop(e: DragEvent): void {
  dragging.value = false
  if (e.dataTransfer?.files) void uploadFiles(Array.from(e.dataTransfer.files))
}

defineExpose({ uploadFiles })
</script>

<template>
  <div
    class="dropzone rounded-md border-2 border-dashed border-border"
    :class="{ 'is-dragging border-primary': dragging }"
    @dragover.prevent="dragging = true"
    @dragleave.prevent="dragging = false"
    @drop.prevent="onDrop"
  >
    <label class="dropzone__label">
      <span>{{ $t('media.dropzone') }}</span>
      <input type="file" multiple class="dropzone__input" @change="onInput" />
    </label>
    <ul v-if="rows.length" class="dropzone__rows">
      <li v-for="(r, i) in rows" :key="i" :class="[r.state, { 'text-muted-foreground': r.state === 'uploading' }]">
        {{ r.name }}<template v-if="r.error"> — {{ r.error }}</template>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.dropzone {
  display: flex;
  flex-direction: column;
  /* Large, roughly-square drop target so files are easy to drag in; capped so it never
     overflows shorter viewports. Width is driven by the dialog (~78vw). */
  min-height: min(68vh, 680px);
  padding: 24px;
  text-align: center;
  background: var(--surface);
  transition: border-color var(--speed, .15s), background-color var(--speed, .15s);
}

.dropzone.is-dragging {
  background: var(--surface-2);
}

.dropzone__label {
  /* Fill the whole tall dropzone so clicking/dropping anywhere in the area works. */
  flex: 1;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  cursor: pointer;
}

.dropzone__input {
  display: none;
}

.dropzone__rows {
  list-style: none;
  margin: 12px 0 0;
  padding: 0;
  text-align: left;
}

.dropzone__rows li.error {
  color: var(--danger);
}
</style>
