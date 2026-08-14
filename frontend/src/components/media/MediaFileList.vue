<script setup lang="ts">
import { useSlots } from 'vue'
import { Folder, Pencil, Trash2 } from '@lucide/vue'
import { Button } from '@/components/ui/button'
import FileThumbnail, { type FileRow } from './FileThumbnail.vue'
import { formatFileSize } from '../../lib/formatFileSize'
import { formatDateTime } from '../../lib/formatDateTime'
import type { FolderRow } from '../../lib/folderTree'

const props = withDefaults(defineProps<{
  files: FileRow[]
  folders?: FolderRow[]
  canManageFolders?: boolean
}>(), { folders: () => [], canManageFolders: false })

const emit = defineEmits<{
  (e: 'open', id: string): void
  (e: 'openFolder', id: string): void
  (e: 'renameFolder', folder: FolderRow): void
  (e: 'removeFolder', folder: FolderRow): void
}>()

const slots = useSlots()
// The actions column exists when the caller supplied its own #actions slot (trash mode's
// restore/purge) OR when there are folder rows to show manage buttons for. Both the header
// <th> and every body <td> call this SAME function so the column count can never diverge
// between <thead> and <tbody>.
//
// This MUST stay a plain function, not a computed. `useSlots()` returns `instance.slots`, a
// plain object Vue mutates in place (via `updateSlots()`) rather than a reactive source --
// reading `slots.actions` inside a `computed` cannot be tracked, so the computed would only
// re-evaluate when one of its OTHER, genuinely-reactive deps (`props.folders`/
// `props.canManageFolders`) changes. This component survives an active<->trash mode toggle
// (it's only `v-else`'d on `view`, not on `mode`), so a `computed` version would go stale the
// moment the parent ever hands it a stable `folders` array identity across that toggle --
// the restore/purge actions would silently vanish with no change to the column count. Calling
// a plain function on every render reads `slots.actions` fresh every time and has no such trap.
function showActionsColumn(): boolean {
  return !!slots.actions || (props.folders.length > 0 && props.canManageFolders)
}

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
        <th class="media-list__thumb-col text-muted-foreground" aria-hidden="true"></th>
        <th class="text-muted-foreground">{{ $t('media.colName') }}</th>
        <th class="text-muted-foreground">{{ $t('media.colType') }}</th>
        <th class="text-muted-foreground">{{ $t('media.colSize') }}</th>
        <th class="text-muted-foreground">{{ $t('media.colDimensions') }}</th>
        <th class="text-muted-foreground">{{ $t('media.colUploaded') }}</th>
        <th v-if="showActionsColumn()" class="media-list__actions-col text-muted-foreground"></th>
      </tr>
    </thead>
    <tbody>
      <tr v-for="d in folders" :key="`folder-${d.id}`" class="media-list__row" @click="emit('openFolder', d.id)">
        <td class="media-list__thumb">
          <Folder class="size-5 text-primary" aria-hidden="true" />
        </td>
        <td class="media-list__name">
          <button
            type="button"
            class="media-list__open focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary"
            @click.stop="emit('openFolder', d.id)"
          >
            {{ d.name }}
          </button>
        </td>
        <td>{{ $t('media.colTypeFolder') }}</td>
        <td>—</td>
        <td>—</td>
        <td>—</td>
        <td v-if="showActionsColumn()" class="media-list__actions">
          <template v-if="canManageFolders">
            <Button type="button" variant="ghost" size="icon-sm" :aria-label="$t('media.folderRename')"
                    @click.stop="emit('renameFolder', d)">
              <Pencil aria-hidden="true" />
            </Button>
            <Button type="button" variant="ghost" size="icon-sm" class="text-destructive hover:text-destructive"
                    :aria-label="$t('media.folderDelete')" @click.stop="emit('removeFolder', d)">
              <Trash2 aria-hidden="true" />
            </Button>
          </template>
        </td>
      </tr>
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
        <td class="media-list__thumb"><FileThumbnail :file="f" size="sm" /></td>
        <td class="media-list__name">
          <button
            type="button"
            class="media-list__open focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-primary"
            @click.stop="emit('open', f.id)"
          >
            {{ f.fileName }}
          </button>
        </td>
        <td>{{ f.contentType }}</td>
        <td>{{ formatFileSize(f.size) }}</td>
        <td>{{ dims(f) }}</td>
        <td>{{ uploaded(f) }}</td>
        <td v-if="showActionsColumn()" class="media-list__actions">
          <slot name="actions" :file="f" />
        </td>
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
  font-weight: 600;
  border-bottom: 1px solid var(--border);
}
.media-list__row {
  cursor: pointer;
  transition: background var(--speed, .15s);
}
.media-list__row:hover { background: var(--surface-2); }
.media-list td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--border);
  color: var(--fg);
  vertical-align: middle;
}
.media-list__thumb-col { width: 64px; }
.media-list__thumb { width: 56px; }
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
.media-list__actions-col { width: 6rem; }
.media-list__actions { display: flex; gap: 4px; justify-content: flex-end; }
</style>
