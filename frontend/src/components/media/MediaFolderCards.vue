<script setup lang="ts">
import { Folder, Pencil, Trash2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import type { FolderRow } from '../../lib/folderTree'

defineProps<{ folders: FolderRow[]; canManage: boolean }>()
const emit = defineEmits<{
  (e: 'open', id: string): void
  (e: 'rename', folder: FolderRow): void
  (e: 'remove', folder: FolderRow): void
}>()
</script>

<template>
  <div v-if="folders.length" class="folder-grid">
    <div v-for="f in folders" :key="f.id" class="folder-card" role="button" tabindex="0"
         @click="emit('open', f.id)" @keydown.enter.self="emit('open', f.id)">
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
  border: 1px solid var(--border); border-radius: var(--legacy-radius-lg, 12px); background: var(--surface);
  transition: border-color var(--speed, .15s), box-shadow var(--speed, .15s);
}
.folder-card:hover { border-color: var(--legacy-accent); box-shadow: var(--shadow-1); }
.folder-card__name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .9rem; }
</style>
