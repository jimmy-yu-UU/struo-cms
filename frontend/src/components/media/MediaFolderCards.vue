<script setup lang="ts">
import { ref } from 'vue'
import { Folder, Pencil, Trash2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import type { FolderRow } from '../../lib/folderTree'
import { setDragPayload, isMediaDrag, readDragPayload } from '../../lib/mediaDnd'
import type { MovePayload } from '../../lib/mediaMove'

defineProps<{ folders: FolderRow[]; canManage: boolean; canMove?: boolean }>()
const emit = defineEmits<{
  (e: 'open', id: string): void
  (e: 'rename', folder: FolderRow): void
  (e: 'remove', folder: FolderRow): void
  (e: 'dropOn', targetFolderId: string, payload: MovePayload): void
}>()

const droppingId = ref<string | null>(null)

function onDragStart(ev: DragEvent, f: FolderRow): void {
  setDragPayload(ev, { files: [], folders: [f.id] })
}
function onDragOver(ev: DragEvent, id: string): void {
  if (!isMediaDrag(ev)) return
  droppingId.value = id
}
function onDrop(ev: DragEvent, id: string): void {
  droppingId.value = null
  const payload = readDragPayload(ev)
  if (payload) emit('dropOn', id, payload)
}
</script>

<template>
  <div v-if="folders.length" class="folder-grid">
    <div v-for="f in folders" :key="f.id" class="folder-card rounded-xl border border-border hover:border-primary" role="button" tabindex="0"
         :draggable="canMove ? 'true' : undefined"
         :data-dropping="droppingId === f.id ? 'true' : undefined"
         @click="emit('open', f.id)" @keydown.enter.self="emit('open', f.id)"
         @dragstart="onDragStart($event, f)"
         @dragover.prevent="onDragOver($event, f.id)"
         @dragleave="droppingId = null"
         @drop.prevent="onDrop($event, f.id)">
      <Folder class="folder-card__icon size-4 shrink-0 text-primary" aria-hidden="true" />
      <span class="folder-card__name">{{ f.name }}</span>
      <span v-if="canManage" class="folder-card__actions flex">
        <Button type="button" variant="ghost" size="icon-sm" :aria-label="$t('media.folderRename')"
                @click.stop="emit('rename', f)">
          <Pencil aria-hidden="true" />
        </Button>
        <Button type="button" variant="ghost" size="icon-sm"
                class="text-destructive hover:text-destructive"
                :aria-label="$t('media.folderDelete')"
                @click.stop="emit('remove', f)">
          <Trash2 aria-hidden="true" />
        </Button>
      </span>
    </div>
  </div>
</template>

<style scoped>
.folder-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 12px; margin-bottom: 16px; }
.folder-card {
  display: flex; align-items: center; gap: 10px; padding: 10px 12px; cursor: pointer;
  background: var(--surface);
  transition: border-color var(--speed, .15s), box-shadow var(--speed, .15s);
}
.folder-card:hover { box-shadow: var(--shadow-1); }
.folder-card__name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .9rem; }
.folder-card[data-dropping='true'] { outline: 2px solid var(--primary); outline-offset: -2px; }
</style>
