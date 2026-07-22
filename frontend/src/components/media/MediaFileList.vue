<script setup lang="ts">
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'
import { formatFileSize } from '../../lib/formatFileSize'

defineProps<{ files: FileRow[] }>()
const emit = defineEmits<{ (e: 'open', id: string): void }>()

function dims(f: FileRow): string {
  return f.width && f.height ? `${f.width}×${f.height}` : '—'
}
function uploaded(f: FileRow): string {
  return f.createdAt ? new Date(f.createdAt).toLocaleDateString() : '—'
}
// FE-25: the row is a clickable target (mirrors MediaGrid's real <button> tiles) but a native
// <tr> has no built-in keyboard affordance, so wire Enter/Space to the same 'open' emit.
function onKeydown(e: KeyboardEvent, id: string): void {
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault()
    emit('open', id)
  }
}
</script>

<template>
  <table class="media-list">
    <thead>
      <tr>
        <th class="media-list__thumb-col" aria-hidden="true"></th>
        <th>{{ $t('media.colName') }}</th>
        <th>{{ $t('media.colType') }}</th>
        <th>{{ $t('media.colSize') }}</th>
        <th>{{ $t('media.colDimensions') }}</th>
        <th>{{ $t('media.colUploaded') }}</th>
      </tr>
    </thead>
    <tbody>
      <tr
        v-for="f in files"
        :key="f.id"
        class="media-list__row"
        role="button"
        tabindex="0"
        @click="emit('open', f.id)"
        @keydown="onKeydown($event, f.id)"
      >
        <td class="media-list__thumb"><FileThumbnail :file="f" /></td>
        <td class="media-list__name">{{ f.fileName }}</td>
        <td>{{ f.contentType }}</td>
        <td>{{ formatFileSize(f.size) }}</td>
        <td>{{ dims(f) }}</td>
        <td>{{ uploaded(f) }}</td>
      </tr>
    </tbody>
  </table>
</template>

<style scoped>
.media-list {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}
.media-list th {
  text-align: left;
  padding: 8px 12px;
  color: var(--muted);
  font-weight: 600;
  border-bottom: 1px solid var(--border);
}
.media-list__row {
  cursor: pointer;
  transition: background var(--speed, .15s);
}
.media-list__row:hover { background: var(--bg); }
.media-list td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--border);
  color: var(--fg);
  vertical-align: middle;
}
.media-list__thumb-col { width: 64px; }
.media-list__thumb { width: 56px; }
.media-list__thumb :deep(.file-thumb) { height: 44px; width: 56px; }
.media-list__name { font-weight: 500; }
</style>
