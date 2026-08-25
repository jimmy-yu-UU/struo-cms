<script setup lang="ts">
import { ref, watch, onBeforeUnmount } from 'vue'
import { useI18n } from 'vue-i18n'
import { useEditor, EditorContent } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'
import { TableKit } from '@tiptap/extension-table'
import TextAlign from '@tiptap/extension-text-align'
import { TextStyle, Color } from '@tiptap/extension-text-style'
import Subscript from '@tiptap/extension-subscript'
import Superscript from '@tiptap/extension-superscript'
import { Placeholder } from '@tiptap/extensions'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import MediaGrid from '../media/MediaGrid.vue'
import RichTextCommandButton from './RichTextCommandButton.vue'
import RichTextColorMenu from './RichTextColorMenu.vue'
import RichTextHeadingMenu from './RichTextHeadingMenu.vue'
import RichTextTableMenu from './RichTextTableMenu.vue'
import RichTextTableContextMenu from './RichTextTableContextMenu.vue'
import RichTextTableSizeDialog from './RichTextTableSizeDialog.vue'
import {
  TOOLBAR_BEFORE_HEADINGS, TOOLBAR_BEFORE_COLOR, TOOLBAR_AFTER_TABLE,
  type RichTextCommand, type RichTextCommandContext,
} from './richTextCommands'
import { HEADING_LEVELS, type HeadingLevel } from './richTextHeadings'
import { isInEditorTable, type TableAction } from './richTextTableActions'
import type { FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import { fileContentDisplayUrl, absolutizeImageSrc, relativizeImageSrc } from '../../lib/richTextImages'
import { debounce } from '../../lib/debounce'
import { createLatestWins } from '../../lib/latestWins'
import { toFileRows } from '../../lib/toFileRow'

defineOptions({ name: 'RichTextInput' })

const props = defineProps<{ modelValue: string; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: string): void }>()

const { t, locale } = useI18n()
const langStore = useLanguageStore()
const imageDialogOpen = ref(false)
const files = ref<FileRow[]>([])
const imageSearch = ref('')
const imageError = ref('')

const imagesLoad = createLatestWins()
async function loadImages(): Promise<void> {
  const token = imagesLoad.next()
  imageError.value = ''
  try {
    const res = await itemsApi.list('file', {
      page: 0, rows: 50, search: imageSearch.value || undefined,
      locale: langStore.defaultCode || undefined,
    })
    if (!imagesLoad.isCurrent(token)) return
    files.value = toFileRows(res.data)
  } catch (e) {
    if (!imagesLoad.isCurrent(token)) return
    imageError.value = e instanceof Error ? e.message : t('fields.loadFilesFailed')
  }
}
// Debounce only search-driven reloads; openImageDialog's direct loadImages() stays immediate.
const debouncedLoadImages = debounce(loadImages, 300)

async function openImageDialog(): Promise<void> {
  imageDialogOpen.value = true
  await loadImages()
}

const commandContext: RichTextCommandContext = {
  // vue-i18n's t is heavily overloaded; the registry only ever needs the single-key form.
  t: (key: string) => t(key),
  openImageDialog: () => { void openImageDialog() },
}

function runCommand(command: RichTextCommand): void {
  if (!editor.value) return
  command.run(editor.value, commandContext)
}

// Re-derive data-file-id from managed src, then relativize before emitting.
function withFileIds(html: string): string {
  if (!html) return html
  const doc = new DOMParser().parseFromString(html, 'text/html')
  doc.querySelectorAll('img').forEach((img) => {
    if (img.getAttribute('data-file-id')) return
    const m = (img.getAttribute('src') || '').match(/\/files\/([^/]+)\/content/)
    if (m) img.setAttribute('data-file-id', m[1])
  })
  return doc.body.innerHTML
}

function emitNormalized(): void {
  const html = editor.value?.getHTML() ?? ''
  emit('update:modelValue', relativizeImageSrc(withFileIds(html)))
}

function insertImage(id: string, alt = ''): void {
  if (!editor.value) return
  editor.value.chain().focus().setImage({ src: fileContentDisplayUrl(id), alt }).run()
  emitNormalized()
}

function onImageSelected(id: string): void {
  insertImage(id)
  imageDialogOpen.value = false
}

const editor = useEditor({
  content: absolutizeImageSrc(props.modelValue || ''),
  editable: !props.disabled,
  // Applied to the contenteditable element itself, not the outer .rich-text frame: the frame
  // stays full width for its border/toolbar while the content measure caps well short of it,
  // leaving the padded box wider than the editable — the @click.self handler below hands a
  // click landing in that gap off to the editor instead of leaving it dead.
  editorProps: { attributes: { class: 'prose dark:prose-invert' } },
  extensions: [
    StarterKit.configure({ heading: { levels: [...HEADING_LEVELS] }, underline: false, link: false }),
    Link.configure({ openOnClick: false, protocols: ['http', 'https', 'mailto'], autolink: false }),
    Image.configure({ inline: false }),
    TableKit.configure({ table: { resizable: false } }),
    TextAlign.configure({ types: ['heading', 'paragraph'], alignments: ['left', 'center', 'right', 'justify'] }),
    TextStyle,
    Color,
    Subscript.extend({ excludes: 'superscript' }),
    Superscript.extend({ excludes: 'subscript' }),
    Placeholder.configure({ placeholder: () => t('fields.richtext.placeholder') }),
  ],
  onUpdate: () => emitNormalized(),
})

// Placeholder text is delivered as a ProseMirror decoration, and decorations only recompute when
// editor state changes. Switching the UI locale dispatches nothing, so nudge the view with an
// empty transaction to force the decoration to be rebuilt with the new string.
watch(locale, () => {
  const ed = editor.value
  if (!ed) return
  ed.view.dispatch(ed.state.tr)
})

// Keep the editor in sync with external model changes without clobbering the cursor.
watch(() => props.modelValue, (val) => {
  const current = relativizeImageSrc(withFileIds(editor.value?.getHTML() ?? ''))
  if (editor.value && (val || '') !== current) {
    editor.value.commands.setContent(absolutizeImageSrc(val || ''), { emitUpdate: false })
  }
})
watch(() => props.disabled, (d) => editor.value?.setEditable(!d))

onBeforeUnmount(() => {
  editor.value?.destroy()
  debouncedLoadImages.cancel()
})

function activeHeadingLevel(): HeadingLevel | null {
  const ed = editor.value
  if (!ed) return null
  return HEADING_LEVELS.find((lvl) => ed.isActive('heading', { level: lvl })) ?? null
}

function onHeadingSelect(level: HeadingLevel | null): void {
  if (!editor.value) return
  const chain = editor.value.chain().focus()
  // setHeading, not toggleHeading: this dropdown marks the current level as selected and offers an
  // explicit "Body text" item, so every item must be idempotent -- re-picking the highlighted level
  // has to leave the block at that level, not toggle it back to a paragraph.
  if (level === null) chain.setParagraph().run()
  else chain.setHeading({ level }).run()
}

function onTableAction(action: TableAction): void {
  if (!editor.value) return
  const chain = editor.value.chain().focus()
  const commands: Record<TableAction, () => void> = {
    addRowBefore: () => chain.addRowBefore().run(),
    addRowAfter: () => chain.addRowAfter().run(),
    addColumnBefore: () => chain.addColumnBefore().run(),
    addColumnAfter: () => chain.addColumnAfter().run(),
    deleteRow: () => chain.deleteRow().run(),
    deleteColumn: () => chain.deleteColumn().run(),
    toggleHeaderRow: () => chain.toggleHeaderRow().run(),
    deleteTable: () => chain.deleteTable().run(),
  }
  commands[action]()
}

function onTableInsert(size: { rows: number; cols: number; withHeaderRow: boolean }): void {
  editor.value?.chain().focus()
    .insertTable({ rows: size.rows, cols: size.cols, withHeaderRow: size.withHeaderRow }).run()
}

// Opened by the table menu's "custom size…" entry; RichTextTableSizeDialog below reads it.
const sizeDialogOpen = ref(false)

const contentRoot = ref<HTMLElement | null>(null)

// Runs in the CAPTURE phase on a wrapper that is a STRICT ANCESTOR of reka's own trigger element
// (see the template below), and decides synchronously which menu the user gets:
//
//   - no editor yet, or outside a table: stopPropagation, so reka never sees the event and nothing
//     calls preventDefault -- the browser's own menu (spellcheck, paste) appears untouched. This
//     also stops ProseMirror's OWN contextmenu handler, which prosemirror-view registers on
//     view.dom (a descendant of this wrapper) purely to force-flush a pending IME composition
//     before the native menu opens (`handlers.contextmenu = view => forceDOMFlush(view)`, itself
//     `endComposition(view)`, in prosemirror-view/dist/index.js). The narrow, accepted consequence
//     of suppressing that here is a possibly-stale native menu mid-composition -- nothing about
//     table state.
//   - inside a table on a disabled (read-only) field: let the event continue on its own, doing
//     nothing here. RichTextTableContextMenu already forwards `disabled` to its own
//     ContextMenuTrigger, which will decline to open ours and fall back to the native menu, so
//     there is nothing left for this handler to add -- and a disabled/read-only editor should not
//     have its selection moved at all, which is why the focus-the-cell step below is skipped too.
//   - inside a table on an enabled field: move the selection into the clicked cell, then let the
//     event bubble on so reka opens ours.
//
// Two independent reasons to prefer stopPropagation over driving reka's own `disabled` prop, not
// one. Shape: `disabled` is a component-lifetime prop, while the decision here is per-event (which
// cell, if any, was clicked) -- a prop is the wrong vehicle for that regardless of timing. Timing:
// checked the installed reka-ui@2.10.3 source directly (ContextMenuTrigger.js) --
// `handleContextMenu` reads `disabled.value` synchronously as its very first statement, before its
// own `await nextTick()`. That value arrives as a PROP, forwarded through three component
// boundaries (this file's `disabled` -> RichTextTableContextMenu's own `disabled` prop -> the
// vendored ui/context-menu ContextMenuTrigger's `useForwardProps` -> reka's own `toRefs(props)`).
// Vue applies prop updates to a child component on its job queue, a microtask -- and DOM event
// dispatch from the capture phase to the bubble phase is synchronous, so no microtask can run in
// between. A ref flipped in this handler would still read stale at reka's guard, for the same
// event, every time. stopPropagation() sidesteps both problems at once.
//
// The handler MUST sit on an element outside RichTextTableContextMenu, not on the element reka
// binds to. stopPropagation() does not stop other listeners on the SAME element -- only
// stopImmediatePropagation() does, and at-target listeners fire in registration order, which is
// not ours to control. From a strict ancestor, the capture listener always runs first and
// stopPropagation() reliably prevents the event from ever reaching reka.
function onContentContextMenu(e: MouseEvent): void {
  const root = contentRoot.value
  const ed = editor.value
  if (!root || !ed) { e.stopPropagation(); return }
  if (!isInEditorTable(e.target, root)) { e.stopPropagation(); return }
  if (props.disabled) return
  // Commands act on the current selection, so a right-click on a cell the caret is not in would
  // otherwise apply to wherever the caret happens to be. posAtCoords needs layout, so this line
  // cannot be proven in jsdom -- it is verified live (see the plan's Task 7).
  const at = ed.view.posAtCoords({ left: e.clientX, top: e.clientY })
  if (at) ed.commands.focus(at.pos)
}

defineExpose({ editor, insertImage })
</script>

<template>
  <div class="rich-text rounded-md border">
    <div v-if="editor" class="rich-text__toolbar flex flex-wrap gap-1 border-b p-1.5">
      <!--
        Three loops, not one: the heading, colour and table menus are their own components sitting
        at fixed points in the toolbar order, so the registry carries the segments the seams between
        them fall on rather than one flat list plus splice indices.
      -->
      <RichTextCommandButton v-for="cmd in TOOLBAR_BEFORE_HEADINGS" :key="cmd.id" :command="cmd"
        :active="cmd.isActive ? cmd.isActive(editor) : undefined" :disabled="disabled"
        @run="runCommand(cmd)" />
      <RichTextHeadingMenu :disabled="disabled" :active-level="activeHeadingLevel()"
        @select="onHeadingSelect" />
      <RichTextCommandButton v-for="cmd in TOOLBAR_BEFORE_COLOR" :key="cmd.id" :command="cmd"
        :active="cmd.isActive ? cmd.isActive(editor) : undefined" :disabled="disabled"
        @run="runCommand(cmd)" />
      <RichTextColorMenu :disabled="disabled"
        :active-color="(editor.getAttributes('textStyle').color as string | undefined) ?? null"
        @pick="(c: string) => editor!.chain().focus().setColor(c).run()"
        @clear="editor!.chain().focus().unsetColor().run()" />
      <RichTextTableMenu :disabled="disabled" @insert="onTableInsert" @custom-size="sizeDialogOpen = true" />
      <RichTextCommandButton v-for="cmd in TOOLBAR_AFTER_TABLE" :key="cmd.id" :command="cmd"
        :active="cmd.isActive ? cmd.isActive(editor) : undefined" :disabled="disabled"
        @run="runCommand(cmd)" />
    </div>
    <div ref="contentRoot" @contextmenu.capture="onContentContextMenu">
      <RichTextTableContextMenu :disabled="disabled" @action="onTableAction">
        <EditorContent class="rich-text__content min-h-32 p-2.5" :editor="editor"
          @click.self="editor?.chain().focus().run()" />
      </RichTextTableContextMenu>
    </div>
    <!--
      DialogScrollContent, not DialogContent: same defect as FilePicker's file dialog — MediaGrid
      can run to several rows, reka's DialogRoot locks body scroll while open, and plain
      DialogContent is fixed-position/viewport-centered with no scroll container of its own, so
      rows above and below the viewport become unreachable. DialogScrollContent's overlay carries
      its own overflow-y-auto and keeps the content box in normal flow instead.

      Its own width class is an unprefixed max-w-lg (no sm: modifier, unlike plain DialogContent),
      so max-w-4xl below is unprefixed too — matching modifiers is what makes tailwind-merge drop
      the vendored default instead of leaving both classes to fight on source order.
    -->
    <Dialog v-model:open="imageDialogOpen">
      <DialogScrollContent class="max-w-4xl">
        <DialogHeader>
          <DialogTitle>{{ t('fields.richtext.insertImageTitle') }}</DialogTitle>
        </DialogHeader>
        <p v-if="imageError" class="text-destructive" role="alert">{{ imageError }}</p>
        <Input v-model="imageSearch" :placeholder="t('fields.searchFiles')" :aria-label="t('fields.searchFiles')" class="my-1" @update:model-value="debouncedLoadImages" />
        <MediaGrid :files="files" selectable @select="onImageSelected" />
      </DialogScrollContent>
    </Dialog>
    <RichTextTableSizeDialog v-model:open="sizeDialogOpen" @insert="onTableInsert" />
  </div>
</template>

<style scoped>
/* TipTap's own generated DOM, not a vendored ui/ component — styling it here is legitimate. */
.rich-text__content :deep(.ProseMirror) { outline: none; min-height: 6rem; }

/* Placeholder is admin chrome, not article content — a rendered article has no placeholder — so it
   deliberately uses the admin's own token rather than a typography variable. */
.rich-text__content :deep(.ProseMirror .is-editor-empty::before) {
  content: attr(data-placeholder);
  color: var(--muted-foreground);
  float: left;
  height: 0;
  pointer-events: none;
}

/* TipTap's table schema has no thead node: content the server normalized into <thead> is flattened
   back to `tbody > th` the moment it is parsed into the editor, and getHTML() re-serializes it that
   way too. So @tailwindcss/typography's `thead th` rules never match anything in here, and without
   this the editor would show unstyled header cells for content that renders with full header
   treatment once published.
   The values mirror the plugin's own `base` modifier so the two agree. `tbody tr`'s own bottom-border
   rule already fires on this row (it IS a tbody row), but it borrows the wrong token and the wrong
   width: published output puts this row inside `<thead>`, whose bottom border reads
   `--tw-prose-th-borders`, while `tbody tr` reads `--tw-prose-td-borders` instead -- and a
   header-only table publishes as a `<thead>` that keeps its 1px bottom rule, while the same single
   row here is also `tbody tr:last-child`, which zeroes that width to none. The two row-level rules
   below correct both; the plugin puts this border on the row, not the cell, so they sit alongside
   the cell rules rather than folded into them. Padding, colour, weight and alignment are mirrored on
   the cells below. This does not chase every nested rule the plugin defines for table content (e.g.
   `thead th strong`'s `color: inherit`) -- only the row- and cell-level treatment that governs the
   header row's own appearance.
   Scoped to the first row, not every `th`: the server only wraps a first row whose cells are ALL
   `th` (see Task 1). A header row anywhere else -- reachable from the table context menu, since
   prosemirror-tables' toggleHeaderRow toggles whatever row the caret is in, not row 0 -- stays
   `tbody > th` once published, where typography's `thead th` matches nothing. Styling it here too
   would make the editor lie about that: it would show padded, bold, bottom-aligned cells for a row
   that renders unstyled once published. Mirroring the server's own condition keeps the editor
   truthful instead. */
.rich-text__content :deep(.ProseMirror tbody tr:first-child:not(:has(td))) {
  border-bottom-color: var(--tw-prose-th-borders);
}
.rich-text__content :deep(.ProseMirror tbody tr:first-child:not(:has(td)):last-child) {
  border-bottom-width: 1px;
}
.rich-text__content :deep(.ProseMirror tbody tr:first-child:not(:has(td)) th) {
  color: var(--tw-prose-headings);
  font-weight: 600;
  vertical-align: bottom;
  padding-inline-end: 0.5714286em;
  padding-bottom: 0.5714286em;
  padding-inline-start: 0.5714286em;
}
.rich-text__content :deep(.ProseMirror tbody tr:first-child:not(:has(td)) th:first-child) { padding-inline-start: 0; }
.rich-text__content :deep(.ProseMirror tbody tr:first-child:not(:has(td)) th:last-child) { padding-inline-end: 0; }
</style>
