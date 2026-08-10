<script setup lang="ts">
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'
import { formatFileSize } from '../../lib/formatFileSize'
import { formatDateTime } from '../../lib/formatDateTime'

defineProps<{ files: FileRow[] }>()
const emit = defineEmits<{ (e: 'open', id: string): void }>()

function dims(f: FileRow): string {
  return f.width && f.height ? `${f.width}×${f.height}` : '—'
}
function uploaded(f: FileRow): string {
  return formatDateTime(f.createdAt ?? null)
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
      <!--
        A table row is not a button, so it must not claim `role="button"` (that gave
        screen readers contradictory roles). Table semantics stay intact on the <tr>; the real,
        natively keyboard-activatable <button> in the name cell (mirrors MediaGrid's tile
        buttons) is the accessible primary action. The row keeps its own @click purely as a
        mouse convenience so clicking anywhere in the row still opens the item, same as before.
      -->
      <tr
        v-for="f in files"
        :key="f.id"
        class="media-list__row"
        @click="emit('open', f.id)"
      >
        <td class="media-list__thumb"><FileThumbnail :file="f" /></td>
        <td class="media-list__name">
          <button type="button" class="media-list__open" @click.stop="emit('open', f.id)">
            {{ f.fileName }}
          </button>
        </td>
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
  color: var(--legacy-muted);
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
.media-list__name { font-weight: 500; padding: 0; }
.media-list__open {
  display: block;
  width: 100%;
  padding: 8px 12px;
  margin: 0;
  border: 0;
  background: none;
  font: inherit;
  font-weight: inherit;
  color: inherit;
  text-align: left;
  cursor: pointer;
}
.media-list__open:focus-visible {
  outline: 2px solid var(--legacy-accent);
  outline-offset: -2px;
}
</style>
