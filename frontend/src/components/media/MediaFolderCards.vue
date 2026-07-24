<script setup lang="ts">
import Button from 'primevue/button'
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
         @click="emit('open', f.id)" @keydown.enter="emit('open', f.id)">
      <i class="pi pi-folder folder-card__icon" aria-hidden="true" />
      <span class="folder-card__name">{{ f.name }}</span>
      <span v-if="canManage" class="folder-card__actions">
        <Button icon="pi pi-pencil" text size="small" :aria-label="$t('media.folderRename')"
                @click.stop="emit('rename', f)" />
        <Button icon="pi pi-trash" text size="small" severity="danger" :aria-label="$t('media.folderDelete')"
                @click.stop="emit('remove', f)" />
      </span>
    </div>
  </div>
</template>

<style scoped>
.folder-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 12px; margin-bottom: 16px; }
.folder-card {
  display: flex; align-items: center; gap: 10px; padding: 10px 12px; cursor: pointer;
  border: 1px solid var(--border); border-radius: var(--radius-lg, 12px); background: var(--surface);
  transition: border-color var(--speed, .15s), box-shadow var(--speed, .15s);
}
.folder-card:hover { border-color: var(--accent); box-shadow: var(--shadow-1); }
.folder-card__icon { color: var(--accent); font-size: 1.1rem; }
.folder-card__name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: .9rem; }
.folder-card__actions { display: flex; }
</style>
