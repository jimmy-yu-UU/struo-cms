<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import {
  ContextMenu, ContextMenuContent, ContextMenuItem, ContextMenuTrigger,
} from '@/components/ui/context-menu'

// Shared right-click menu for the four media-library surfaces (grid tile, folder card, file row,
// folder row). The host component (MediaGrid/MediaFolderCards/MediaFileList) knows which item was
// clicked and owns the actual file/folder object -- this component only renders the menu shape and
// signals which action was picked; the host translates that into its own existing `open`/`rename`/
// `remove` emits (already consumed by MediaLibraryView) or the new `requestMove` payload.
const props = defineProps<{
  kind: 'file' | 'folder'
  canMove?: boolean
  canDelete?: boolean
  // Folder-only: gates the Rename entry. Ignored for kind === 'file'.
  canRename?: boolean
  // Forwarded to reka's ContextMenuTrigger: true restores the native browser menu and suppresses
  // ours entirely (used for trashed items -- see MediaGrid/MediaFileList).
  disabled?: boolean
}>()
const emit = defineEmits<{
  (e: 'open'): void
  (e: 'move'): void
  (e: 'rename'): void
  (e: 'remove'): void
}>()

const { t } = useI18n()

// These re-checks are the actual guard, not the `v-if` on the entry below. reka's own MenuItem
// already refuses to fire `@select` when `disabled` is set, but this component must not depend on
// that third-party behaviour as its only line of defence -- see MediaMoveDialog's `choose()` for
// the same reasoning. Each handler is exposed so a test can call it directly with the permission
// off, bypassing the DOM entirely, the same way MediaMoveDialog's test calls `choose()` on a
// disabled option.
function onOpen(): void { emit('open') }
function onMove(): void { if (!props.canMove) return; emit('move') }
function onRename(): void { if (!props.canRename) return; emit('rename') }
function onRemove(): void { if (!props.canDelete) return; emit('remove') }

defineExpose({ onMove, onRename, onRemove })
</script>

<template>
  <ContextMenu>
    <ContextMenuTrigger as-child :disabled="disabled">
      <slot />
    </ContextMenuTrigger>
    <ContextMenuContent>
      <ContextMenuItem data-test="menu-open" @select="onOpen">{{ t('media.menuOpen') }}</ContextMenuItem>
      <ContextMenuItem v-if="kind === 'folder' && canRename" data-test="menu-rename" @select="onRename">
        {{ t('media.menuRename') }}
      </ContextMenuItem>
      <ContextMenuItem v-if="canMove" data-test="menu-move" @select="onMove">
        {{ t('media.menuMove') }}
      </ContextMenuItem>
      <!--
        `variant="destructive"` alone resolves to `data-[variant=destructive]:text-destructive-
        foreground`, and tokens.css deliberately leaves `--color-destructive-foreground` out of
        its `@theme` (see that file's own comment). Verified against the actual build output
        (`pnpm build` then grepped dist/assets/index-*.css): with no matching `--color-*` theme
        token, Tailwind v4 never emits ANY CSS rule for `text-destructive-foreground` at all --
        zero occurrences of "destructive-foreground" anywhere in the compiled stylesheet, scoped
        or not -- so Delete silently keeps the popover's ordinary foreground instead of red.
        `ui/**` is generated and must not be hand-edited, so restore the actual red text at this
        call site instead: `.text-destructive{color:var(--destructive)}` already exists as a
        plain global rule (confirmed in the same build output) and there is no competing
        `text-destructive-foreground` rule to lose a specificity contest against, so a plain
        (non-important) override is sufficient here.
      -->
      <ContextMenuItem v-if="canDelete" data-test="menu-delete" variant="destructive" class="text-destructive" @select="onRemove">
        {{ t('media.menuDelete') }}
      </ContextMenuItem>
    </ContextMenuContent>
  </ContextMenu>
</template>
