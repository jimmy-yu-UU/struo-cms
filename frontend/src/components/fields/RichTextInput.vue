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
import {
  AlignLeft, AlignCenter, AlignRight, AlignJustify, List, ListOrdered,
  Quote, Code2, Link as LinkIcon, Minus, Image as ImageIcon, Undo2, Redo2,
} from '@lucide/vue'
import { Button } from '@/components/ui/button'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import MediaGrid from '../media/MediaGrid.vue'
import RichTextColorMenu from './RichTextColorMenu.vue'
import RichTextHeadingMenu from './RichTextHeadingMenu.vue'
import RichTextTableMenu from './RichTextTableMenu.vue'
import RichTextTableContextMenu from './RichTextTableContextMenu.vue'
import { HEADING_LEVELS, type HeadingLevel } from './richTextHeadings'
import { isInEditorTable, type TableAction } from './richTextTableActions'
import type { FileRow } from '../media/FileThumbnail.vue'
import { itemsApi } from '../../api/itemsApi'
import { useLanguageStore } from '../../stores/languageStore'
import { fileContentDisplayUrl, absolutizeImageSrc, relativizeImageSrc } from '../../lib/richTextImages'
import { isAllowedLinkUrl } from '../../lib/linkUrl'
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

const alignKey = {
  left: 'alignLeft', center: 'alignCenter', right: 'alignRight', justify: 'alignJustify',
} as const
const alignIcon = { left: AlignLeft, center: AlignCenter, right: AlignRight, justify: AlignJustify } as const

function setLink(): void {
  if (!editor.value) return
  const prev = editor.value.getAttributes('link').href as string | undefined
  const url = window.prompt(t('fields.richtext.linkPrompt'), prev ?? 'https://')
  if (url === null) return
  if (url === '') { editor.value.chain().focus().unsetLink().run(); return }
  if (!isAllowedLinkUrl(url)) return // defense-in-depth: silently reject javascript:/data:/etc.
  editor.value.chain().focus().extendMarkRange('link').setLink({ href: url }).run()
}

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
    insert: () => chain.insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
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
// stopPropagation, not driving reka's own `disabled` prop, because that prop is shaped for a
// component-lifetime setting while this handler's decision is per-event (which cell, if any, was
// clicked) -- routing every right-click through a ref this handler flips before dispatch would be
// a more roundabout way to express what a synchronous stopPropagation already says directly.
// (Checked the installed reka-ui@2.10.3 source directly: ContextMenuTrigger's handleContextMenu
// reads `disabled.value` synchronously as its very first statement, before any `await` in the
// function -- so even a same-tick ref write here would already be visible to it, since this
// capture-phase handler always finishes before that bubble-phase handler starts for the same
// event. There is no staleness to route around either way; the reason to prefer stopPropagation is
// the shape mismatch above, not a timing race.)
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
        Every active-state toolbar button below re-supplies data-[active=true]:hover:bg-primary
        (and its dark:-prefixed form) alongside the plain data-[active=true]:bg-primary. The ghost
        variant's own hover:bg-accent hover:text-accent-foreground carries just one modifier
        (hover), so an override written with just one modifier (data-[active=true]) ties it on CSS
        specificity and the winner is whichever rule the stylesheet happens to emit later — not a
        reliable outcome. Stacking data-[active=true] AND hover onto the override selector adds an
        attribute-selector component that the plain hover rule lacks, so it wins on specificity
        regardless of emission order. Dark mode needs a second, dark:-prefixed copy of the
        background rule because the vendored ghost variant carries a dark:hover:bg-accent/50
        override of its own: that selector's :is()-wrapped dark-mode wrapper is itself a
        specificity component, so only a same-shape dark:-prefixed override outweighs it — the
        undecorated data-[active=true]:hover:bg-primary rule would tie it, not beat it. The text
        pairing has no such dark-only competitor (ghost never overrides hover text colour for
        dark), so one undecorated override rule already wins in both colour schemes.
      -->
      <Button type="button" variant="ghost" size="icon" data-cmd="bold" :data-active="editor.isActive('bold')"
        :disabled="disabled" :aria-label="t('fields.richtext.bold')" :title="t('fields.richtext.bold')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="editor!.chain().focus().toggleBold().run()"><b>B</b></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="italic" :data-active="editor.isActive('italic')"
        :disabled="disabled" :aria-label="t('fields.richtext.italic')" :title="t('fields.richtext.italic')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="editor!.chain().focus().toggleItalic().run()"><i>I</i></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="strike" :data-active="editor.isActive('strike')"
        :disabled="disabled" :aria-label="t('fields.richtext.strikethrough')" :title="t('fields.richtext.strikethrough')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="editor!.chain().focus().toggleStrike().run()"><s>S</s></Button>
      <Button v-for="al in (['left', 'center', 'right', 'justify'] as const)" :key="al" type="button" variant="ghost" size="icon"
        :data-cmd="`align${al.charAt(0).toUpperCase()}${al.slice(1)}`"
        :data-active="editor.isActive({ textAlign: al })" :disabled="disabled"
        :aria-label="t('fields.richtext.' + alignKey[al])" :title="t('fields.richtext.' + alignKey[al])"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary"
        @click="editor!.chain().focus().setTextAlign(al).run()"><component :is="alignIcon[al]" /></Button>
      <RichTextHeadingMenu :disabled="disabled" :active-level="activeHeadingLevel()"
        @select="onHeadingSelect" />
      <Button type="button" variant="ghost" size="icon" data-cmd="subscript" :data-active="editor.isActive('subscript')"
        :disabled="disabled" :aria-label="t('fields.richtext.subscript')" :title="t('fields.richtext.subscript')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="editor!.chain().focus().toggleSubscript().run()">x₂</Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="superscript" :data-active="editor.isActive('superscript')"
        :disabled="disabled" :aria-label="t('fields.richtext.superscript')" :title="t('fields.richtext.superscript')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="editor!.chain().focus().toggleSuperscript().run()">x²</Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="bulletList" :data-active="editor.isActive('bulletList')"
        :disabled="disabled" :aria-label="t('fields.richtext.bulletList')" :title="t('fields.richtext.bulletList')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="editor!.chain().focus().toggleBulletList().run()"><List /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="orderedList" :data-active="editor.isActive('orderedList')"
        :disabled="disabled" :aria-label="t('fields.richtext.numberedList')" :title="t('fields.richtext.numberedList')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="editor!.chain().focus().toggleOrderedList().run()"><ListOrdered /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="blockquote" :data-active="editor.isActive('blockquote')"
        :disabled="disabled" :aria-label="t('fields.richtext.blockquote')" :title="t('fields.richtext.blockquote')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="editor!.chain().focus().toggleBlockquote().run()"><Quote /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="codeBlock" :data-active="editor.isActive('codeBlock')"
        :disabled="disabled" :aria-label="t('fields.richtext.codeBlock')" :title="t('fields.richtext.codeBlock')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="editor!.chain().focus().toggleCodeBlock().run()"><Code2 /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="link" :data-active="editor.isActive('link')"
        :disabled="disabled" :aria-label="t('fields.richtext.link')" :title="t('fields.richtext.link')"
        class="data-[active=true]:bg-primary data-[active=true]:text-primary-foreground data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground dark:data-[active=true]:hover:bg-primary" @click="setLink"><LinkIcon /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="hr" :disabled="disabled"
        :aria-label="t('fields.richtext.horizontalRule')" :title="t('fields.richtext.horizontalRule')" @click="editor!.chain().focus().setHorizontalRule().run()"><Minus /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="image" :disabled="disabled"
        :aria-label="t('fields.richtext.insertImage')" :title="t('fields.richtext.insertImage')" @click="openImageDialog"><ImageIcon /></Button>
      <RichTextColorMenu :disabled="disabled"
        :active-color="(editor.getAttributes('textStyle').color as string | undefined) ?? null"
        @pick="(c: string) => editor!.chain().focus().setColor(c).run()"
        @clear="editor!.chain().focus().unsetColor().run()" />
      <RichTextTableMenu :disabled="disabled" :in-table="editor.isActive('table')" @action="onTableAction" />
      <Button type="button" variant="ghost" size="icon" data-cmd="undo" :disabled="disabled"
        :aria-label="t('fields.richtext.undo')" :title="t('fields.richtext.undo')" @click="editor!.chain().focus().undo().run()"><Undo2 /></Button>
      <Button type="button" variant="ghost" size="icon" data-cmd="redo" :disabled="disabled"
        :aria-label="t('fields.richtext.redo')" :title="t('fields.richtext.redo')" @click="editor!.chain().focus().redo().run()"><Redo2 /></Button>
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
</style>
