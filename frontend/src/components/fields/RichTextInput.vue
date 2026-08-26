<script setup lang="ts">
import { ref, watch, onBeforeUnmount, nextTick } from 'vue'
import { useI18n } from 'vue-i18n'
import { useEditor, EditorContent, type Editor } from '@tiptap/vue-3'
import StarterKit from '@tiptap/starter-kit'
import Link from '@tiptap/extension-link'
import Image from '@tiptap/extension-image'
import { TableKit } from '@tiptap/extension-table'
import TextAlign from '@tiptap/extension-text-align'
import { TextStyle, Color } from '@tiptap/extension-text-style'
import Subscript from '@tiptap/extension-subscript'
import Superscript from '@tiptap/extension-superscript'
import { Placeholder } from '@tiptap/extensions'
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import MediaGrid from '../media/MediaGrid.vue'
import RichTextBubbleMenu from './RichTextBubbleMenu.vue'
import RichTextCommandButton from './RichTextCommandButton.vue'
import RichTextColorMenu from './RichTextColorMenu.vue'
import RichTextHeadingMenu from './RichTextHeadingMenu.vue'
import RichTextLinkDialog from './RichTextLinkDialog.vue'
import RichTextTableMenu from './RichTextTableMenu.vue'
import RichTextContextMenu from './RichTextContextMenu.vue'
import RichTextTableSizeDialog from './RichTextTableSizeDialog.vue'
import RichTextImageAltDialog from './RichTextImageAltDialog.vue'
import {
  TOOLBAR_BEFORE_HEADINGS, TOOLBAR_BEFORE_COLOR, TOOLBAR_AFTER_TABLE,
  type RichTextCommand, type RichTextCommandContext,
} from './richTextCommands'
import { HEADING_LEVELS, type HeadingLevel } from './richTextHeadings'
import { isInEditorTable, type TableAction } from './richTextTableActions'
import { isEditorImage, type ImageAction } from './richTextImageActions'
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
  blurActiveElementBeforeDialog()
  imageDialogOpen.value = true
  await loadImages()
}

function onImageDialogOpenChange(open: boolean): void {
  imageDialogOpen.value = open
  if (!open) refocusAfterDialogCancel()
}

// Bound to RichTextBubbleMenu's own template ref so openLinkDialog can force it away before the
// dialog takes DOM focus -- see the comment on that call below.
const bubbleMenuRef = ref<InstanceType<typeof RichTextBubbleMenu> | null>(null)

const linkDialogOpen = ref(false)
const linkDialogHref = ref('')
const linkDialogNewTab = ref(false)
const linkDialogCanRemove = ref(false)
// Set for the lifetime of one open dialog; whichever of onLinkDialogSubmit/onLinkDialogRemove/
// onLinkDialogOpenChange(false) runs first resolves it and clears it, so a second settle attempt
// from whichever of those fires afterward (submit and remove both also emit update:open(false)
// right after their own event, per RichTextLinkDialog.vue) is a harmless no-op. That is the only
// thing guaranteed by the three of them alone: neither a second openLinkDialog before this one
// settles, nor unmounting while it is still open, is one of those three, so each is handled
// explicitly below rather than left to this protocol.
let resolveLinkDialog: ((value: { href: string; newTab: boolean } | 'remove' | null) => void) | null = null

function settleLinkDialog(value: { href: string; newTab: boolean } | 'remove' | null): void {
  resolveLinkDialog?.(value)
  resolveLinkDialog = null
}

// Focus handover for every dialog in this file. reka's modal Dialog applies aria-hidden to the
// rest of the page on the same reactive flush that flips `open` true, so an element still holding
// focus at that instant ends up inside the hidden subtree -- it must be blurred first.
// Blurring to <body> also disables reka's own focus restore: it only captures a restore target
// when activeElement is not <body>, so this pair is the only thing putting focus back on a cancel.
// Paths that close by deliberately refocusing the editor (submit, remove, insert, select) clear
// this themselves, so a restore never fights a focus move that was intended.
let restoreFocusOnDialogCancel: (() => void) | null = null

// Focusing a node that has since left the document is a silent no-op that leaves focus on <body>,
// and several callers' captured elements (menu items, the bubble menu's link button) are unmounted
// by the time a cancel runs -- so the editor is the fallback.
function focusIfStillInDocument(el: HTMLElement): () => void {
  return () => {
    if (document.body.contains(el)) el.focus()
    else editor.value?.commands.focus()
  }
}

// `restore`, when given, replaces the default "focus whatever was focused before" capture: the alt
// and table-size dialogs open from a menu/popover item that unmounts on the way in, so the element
// holding focus right now is not a usable restore target for them.
function blurActiveElementBeforeDialog(restore?: () => void): void {
  const active = document.activeElement
  restoreFocusOnDialogCancel = restore
    ?? (active instanceof HTMLElement && active !== document.body ? focusIfStillInDocument(active) : null)
  if (active instanceof HTMLElement) active.blur()
}

// The restore must be deferred a tick, not run synchronously. reka's FocusScope keeps its
// document-level focus-trap listeners attached until Vue flushes the `open: false` prop change
// that tears them down, and a focus() call made before that is caught by the still-active trap
// and pulled straight back into the closing dialog.
function refocusAfterDialogCancel(): void {
  const restore = restoreFocusOnDialogCancel
  restoreFocusOnDialogCancel = null
  if (!restore) return
  void nextTick().then(restore)
}

function openLinkDialog(
  initial: { href: string; newTab: boolean; canRemove: boolean },
): Promise<{ href: string; newTab: boolean } | 'remove' | null> {
  // Not reachable today (only one link command can run at a time), but settle any still-pending
  // prior call with null rather than letting the assignment below silently overwrite
  // resolveLinkDialog and leave that earlier promise unresolved forever.
  settleLinkDialog(null)
  // Capture before the hide() below, not after: afterwards the captured element would be whatever
  // focus fell back to once the bubble menu was torn down.
  blurActiveElementBeforeDialog()
  // Force the bubble menu away (a no-op if it was never showing -- BubbleMenuView.hide() guards
  // on its own isVisible): opening this dialog from its own link button is one of the two entry
  // points sharing this function, and the dialog's autofocus stealing DOM focus from the editor
  // would otherwise leave that menu lingering beside it (RT-5's leftover; see
  // RichTextBubbleMenu.vue's hide() for the mechanism and why it does not depend on this ordering).
  bubbleMenuRef.value?.hide()
  return new Promise((resolve) => {
    resolveLinkDialog = resolve
    // Props set, then the open flip -- both in this same synchronous call, no await between them.
    // RichTextLinkDialog's own reset watch fires on `open` and reads href/newTab/canRemove at that
    // moment; setting them after the flip (even one microtask later) is the one sequence its own
    // watch cannot tell apart from a stale value left over from the previous link.
    linkDialogHref.value = initial.href
    linkDialogNewTab.value = initial.newTab
    linkDialogCanRemove.value = initial.canRemove
    linkDialogOpen.value = true
  })
}

function onLinkDialogSubmit(value: { href: string; newTab: boolean }): void {
  settleLinkDialog(value)
  restoreFocusOnDialogCancel = null
}

function onLinkDialogRemove(): void {
  settleLinkDialog('remove')
  restoreFocusOnDialogCancel = null
}

// Fires for every close, including Cancel, Esc and an overlay click -- not only a plain cancel:
// submit/remove have already settled the promise and cleared the focus capture by the time their
// own trailing update:open(false) reaches here, so both calls below are no-ops for those.
function onLinkDialogOpenChange(open: boolean): void {
  linkDialogOpen.value = open
  if (!open) {
    settleLinkDialog(null)
    refocusAfterDialogCancel()
  }
}

// Not reachable today either (nothing in this repo unmounts a field mid-edit), but unmounting with
// the dialog still open would otherwise leave its promise pending forever -- resolving it with null
// here is the same "cancelled" outcome a plain Cancel click already produces. No focus restore:
// the component is unmounting, so a focus() call scheduled a tick later would hit a torn-down tree.
onBeforeUnmount(() => settleLinkDialog(null))

const commandContext: RichTextCommandContext = {
  openImageDialog: () => { void openImageDialog() },
  openLinkDialog,
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

// setEditable() always emits 'update' -- even when passed the value the editor already holds --
// which onUpdate cannot tell apart from a real edit. Bracket every setEditable() call in this flag
// so it does not emit update:modelValue and fake an unsaved change the user never made.
let suppressEmit = false

function emitNormalized(): void {
  if (suppressEmit) return
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
  restoreFocusOnDialogCancel = null
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
    // HTMLAttributes.target/rel: null, not omitted -- the Link extension's own upstream defaults
    // are target: '_blank' and rel: 'noopener noreferrer nofollow' (its addAttributes() derives
    // each mark attribute's default straight from this option), so every link setLink doesn't
    // explicitly override would otherwise stamp both onto stored HTML. rel is backend-owned
    // (chapter 5's RichText contract table) and target is now a dialog choice per link, not a
    // blanket default.
    Link.configure({
      openOnClick: false, protocols: ['http', 'https', 'mailto'], autolink: false,
      HTMLAttributes: { target: null, rel: null },
    }),
    // `resize.enabled` swaps in @tiptap/core's ResizableNodeView, which is what supplies the drag
    // handles. Upstream defaults: `directions` is the four corners alone, and the aspect ratio is
    // preserved only while Shift is held -- `alwaysPreserveAspectRatio: true` removes that
    // per-drag override entirely, so no handle can free-stretch a published image.
    //
    // `height: { rendered: false }` is not redundant with the backend's height strip. Upstream's
    // onCommit writes both width and height as node attributes after a drag, so without this
    // override getHTML() carries a height the stored value never will -- and the
    // watch(() => props.modelValue) below diffs those two directly, so it would then fire
    // setContent() on every external update and reset the cursor.
    Image.extend({
      addAttributes() {
        return {
          ...this.parent?.(),
          height: { default: null, rendered: false },
        }
      },
    }).configure({
      inline: false,
      resize: {
        enabled: true,
        minWidth: 40,
        alwaysPreserveAspectRatio: true,
        directions: ['top', 'right', 'bottom', 'left', 'top-left', 'top-right', 'bottom-left', 'bottom-right'],
      },
    }),
    TableKit.configure({ table: { resizable: false } }),
    TextAlign.configure({ types: ['heading', 'paragraph'], alignments: ['left', 'center', 'right', 'justify'] }),
    TextStyle,
    Color,
    Subscript.extend({ excludes: 'superscript' }),
    Superscript.extend({ excludes: 'subscript' }),
    Placeholder.configure({ placeholder: () => t('fields.richtext.placeholder') }),
  ],
  onUpdate: () => emitNormalized(),
  // A field that mounts already disabled never runs the watch(() => props.disabled) below, and
  // upstream attaches image resize handles unconditionally, removing them only on the editor's own
  // 'update' event -- so without this a read-only field keeps live handles, and handleResizeStart
  // has no isEditable guard, meaning a drag would emit update:modelValue from it.
  //
  // onCreate rather than a Vue onMounted: @tiptap/vue-3's EditorContent recreates every node view
  // inside its own nextTick() after reparenting the editor's DOM, which would discard a
  // setEditable() call made any earlier.
  onCreate: ({ editor: created }) => {
    if (!props.disabled) return
    suppressEmit = true
    try { created.setEditable(false) } finally { suppressEmit = false }
  },
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
// Same suppression as onCreate above, and required for the same reason: setEditable() emits
// 'update' here too.
watch(() => props.disabled, (d) => {
  const ed = editor.value
  if (!ed) return
  suppressEmit = true
  try { ed.setEditable(!d) } finally { suppressEmit = false }
})

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
  restoreFocusOnDialogCancel = null
}

// Opened by the table menu's "custom size…" entry; RichTextTableSizeDialog below reads it.
const sizeDialogOpen = ref(false)

function openTableSizeDialog(restoreFocusTo: HTMLElement | null): void {
  // The explicit restore is required, not decorative: the "Custom size..." entry that held focus
  // unmounts with its popover, and RichTextTableMenu suppresses that popover's own close-auto-focus
  // on this path (it would otherwise refocus its trigger under this dialog's aria-hidden
  // background) -- so the trigger it hands over here is the only remaining restore target.
  blurActiveElementBeforeDialog(restoreFocusTo ? focusIfStillInDocument(restoreFocusTo) : undefined)
  sizeDialogOpen.value = true
}

function onTableSizeDialogOpenChange(open: boolean): void {
  sizeDialogOpen.value = open
  if (!open) refocusAfterDialogCancel()
}

// Which action list RichTextContextMenu shows for the CURRENT right-click.
const contextMenuTarget = ref<'table' | 'image' | null>(null)

const contentRoot = ref<HTMLElement | null>(null)

// Runs in the CAPTURE phase on a wrapper that is a STRICT ANCESTOR of reka's own trigger element
// (see the template below), and decides synchronously which menu the user gets.
//
// The handler MUST sit outside RichTextContextMenu, not on the element reka binds to:
// stopPropagation() does not stop other listeners on the SAME element, and at-target listeners
// fire in registration order, which is not ours to control. From a strict ancestor the capture
// listener always runs first, so stopPropagation() reliably keeps the event away from reka.
//
// stopPropagation() rather than driving reka's own `disabled` prop, because that prop can never
// arrive in time: reka's handleContextMenu reads `disabled` synchronously as its first statement,
// and Vue applies prop updates on a microtask, which cannot run between capture and bubble.
// Suppressing the event this way also stops ProseMirror's own contextmenu handler, whose only job
// is to flush a pending IME composition -- an accepted trade for a possibly-stale native menu
// mid-composition.
//
// The image check runs BEFORE the table check, and the order is load-bearing: `<td><img></td>` is
// legal content and isInEditorTable would match it too, so the reverse order misroutes a click on
// the image to the table menu.
function onContentContextMenu(e: MouseEvent): void {
  const root = contentRoot.value
  const ed = editor.value
  if (!root || !ed) { e.stopPropagation(); contextMenuTarget.value = null; return }

  const img = isEditorImage(e.target, root)
  if (img) {
    if (props.disabled) { contextMenuTarget.value = null; return }
    // posAtDOM, not posAtCoords like the table branch below: this handler already holds the <img>,
    // so the position needs no layout. The <img> is never the node view's own `dom` (that is the
    // [data-resize-container] wrapper) but it is that container's first descendant, so offset 0
    // resolves to the position immediately before the image node -- which is what
    // setNodeSelection needs.
    contextMenuTarget.value = 'image'
    ed.commands.setNodeSelection(ed.view.posAtDOM(img, 0))
    return
  }

  if (!isInEditorTable(e.target, root)) { e.stopPropagation(); contextMenuTarget.value = null; return }
  if (props.disabled) return
  // Commands act on the current selection, so a right-click on a cell the caret is not in would
  // otherwise apply to wherever the caret happens to be. posAtCoords needs real layout to resolve
  // accurate coordinates, and jsdom lays nothing out -- this line needs a live browser check, not a
  // jsdom test, to confirm the resolved position really does land in the cell under the cursor.
  const at = ed.view.posAtCoords({ left: e.clientX, top: e.clientY })
  if (at) ed.commands.focus(at.pos)
  contextMenuTarget.value = 'table'
}

const imageAltDialogOpen = ref(false)
const imageAltDialogAlt = ref('')

// The image node the alt dialog was opened against; onImageAltDialogSubmit compares against it.
// Structurally typed, and selectedNodeOf duck-types on `node` rather than using
// `instanceof NodeSelection`, because @tiptap/pm is not a direct dependency of this frontend and
// ProseMirror's own types are therefore not nameable here.
type SelectedNode = { type: { name: string } }
let imageAltDialogNode: SelectedNode | null = null

function selectedNodeOf(ed: Editor): SelectedNode | null {
  return (ed.state.selection as { node?: SelectedNode }).node ?? null
}

function onImageAction(action: ImageAction): void {
  const ed = editor.value
  if (!ed) return
  if (action === 'deleteImage') {
    // The context-menu handler above put a NodeSelection on the image, so this deletes that node.
    ed.chain().focus().deleteSelection().run()
    return
  }
  const attrs = ed.getAttributes('image')
  imageAltDialogAlt.value = typeof attrs.alt === 'string' ? attrs.alt : ''
  imageAltDialogNode = selectedNodeOf(ed)
  // The explicit restore is required: the reka context-menu item holding focus right now unmounts
  // with the menu, so the default capture would restore to a detached node. focus() with no
  // position is safe over a NodeSelection -- upstream leaves a non-text selection untouched.
  blurActiveElementBeforeDialog(() => { editor.value?.commands.focus() })
  imageAltDialogOpen.value = true
}

function onImageAltDialogSubmit(alt: string): void {
  const ed = editor.value
  if (!ed) return
  // The guard must compare node IDENTITY, not "is the selection still a NodeSelection on an
  // image". An external modelValue push is reachable while this dialog sits open (autosave,
  // revision revert, language switch) and the watch above answers it with setContent(); ProseMirror
  // then maps the NodeSelection onto the REPLACEMENT document's image, so a type check passes and
  // the alt is written to the wrong picture. Identity separates them because ProseMirror nodes are
  // immutable: an untouched image is handed back as the same object, a setContent() is a new tree.
  // Bailing out silently is deliberate -- the point is only that the wrong image is never written.
  if (!imageAltDialogNode || selectedNodeOf(ed) !== imageAltDialogNode) return
  ed.chain().focus().updateAttributes('image', { alt }).run()
  restoreFocusOnDialogCancel = null
}

function onImageAltDialogOpenChange(open: boolean): void {
  imageAltDialogOpen.value = open
  if (!open) {
    imageAltDialogNode = null
    refocusAfterDialogCancel()
  }
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
        :editor="editor" :disabled="disabled" @run="runCommand(cmd)" />
      <RichTextHeadingMenu :disabled="disabled" :active-level="activeHeadingLevel()"
        @select="onHeadingSelect" />
      <RichTextCommandButton v-for="cmd in TOOLBAR_BEFORE_COLOR" :key="cmd.id" :command="cmd"
        :editor="editor" :disabled="disabled" @run="runCommand(cmd)" />
      <RichTextColorMenu :disabled="disabled"
        :active-color="(editor.getAttributes('textStyle').color as string | undefined) ?? null"
        @pick="(c: string) => editor!.chain().focus().setColor(c).run()"
        @clear="editor!.chain().focus().unsetColor().run()" />
      <RichTextTableMenu :disabled="disabled" @insert="onTableInsert" @custom-size="openTableSizeDialog" />
      <RichTextCommandButton v-for="cmd in TOOLBAR_AFTER_TABLE" :key="cmd.id" :command="cmd"
        :editor="editor" :disabled="disabled" @run="runCommand(cmd)" />
    </div>
    <div ref="contentRoot" @contextmenu.capture="onContentContextMenu">
      <RichTextContextMenu :disabled="disabled" :target="contextMenuTarget"
        @table-action="onTableAction" @image-action="onImageAction">
        <EditorContent class="rich-text__content min-h-32 p-2.5" :editor="editor"
          @click.self="editor?.chain().focus().run()" />
      </RichTextContextMenu>
    </div>
    <!--
      Not inside .rich-text__toolbar: this is a floating overlay that stays out of the DOM until a
      text selection shows it, not a persistent toolbar control -- and RichTextBubbleMenu configures
      BubbleMenuPlugin to relocate its own root into a dedicated container that is itself a child of
      document.body (escaping this column's overflow-x-clip, see AppShell.vue), regardless of where
      it starts in the template, so its position here has no bearing on where -- or under what
      ancestor's event handlers -- it renders once shown.
    -->
    <RichTextBubbleMenu v-if="editor" ref="bubbleMenuRef" :editor="editor" :disabled="disabled" @run="runCommand" />
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
    <Dialog :open="imageDialogOpen" @update:open="onImageDialogOpenChange">
      <DialogScrollContent class="max-w-4xl">
        <DialogHeader>
          <DialogTitle>{{ t('fields.richtext.insertImageTitle') }}</DialogTitle>
          <!--
            Not decoration. reka points DialogContent's aria-describedby at a DialogDescription id
            whether or not one is rendered, and warns on mount when nothing carries that id -- so a
            dialog without one leaves assistive tech following a dangling reference.
          -->
          <DialogDescription>{{ t('fields.richtext.insertImageDescription') }}</DialogDescription>
        </DialogHeader>
        <p v-if="imageError" class="text-destructive" role="alert">{{ imageError }}</p>
        <Input v-model="imageSearch" :placeholder="t('fields.searchFiles')" :aria-label="t('fields.searchFiles')" class="my-1" @update:model-value="debouncedLoadImages" />
        <MediaGrid :files="files" selectable @select="onImageSelected" />
      </DialogScrollContent>
    </Dialog>
    <!-- Explicit @update:open, not v-model sugar: a cancel close also has to restore focus. -->
    <RichTextTableSizeDialog :open="sizeDialogOpen" @update:open="onTableSizeDialogOpenChange" @insert="onTableInsert" />
    <!--
      Explicit @update:open, not v-model sugar: a cancel close has to settle the pending
      openLinkDialog promise with null AND restore focus. Both calls are required; dropping either
      one breaks this path.
    -->
    <RichTextLinkDialog :open="linkDialogOpen" :href="linkDialogHref" :new-tab="linkDialogNewTab"
      :can-remove="linkDialogCanRemove" @update:open="onLinkDialogOpenChange"
      @submit="onLinkDialogSubmit" @remove="onLinkDialogRemove" />
    <!-- Explicit @update:open, not v-model sugar: a cancel close also has to restore focus. -->
    <RichTextImageAltDialog :open="imageAltDialogOpen" @update:open="onImageAltDialogOpenChange" :alt="imageAltDialogAlt"
      @submit="onImageAltDialogSubmit" />
  </div>
</template>

<style scoped>
/* Vue propagates this file's own scope id to a child component's root element (so the toolbar's
   <button>, RichTextCommandButton's root, still carries it), but not to elements further inside
   that child's own template -- RichTextCommandButton has no <style scoped> of its own, so the
   lucide <svg> it renders carries no data-v-* at all. Every rule below happens to target
   `.rich-text__content :deep(...)`, none of them the toolbar, so this is inert today -- but a rule
   added here to style a toolbar icon would silently fail to match it. */

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

/* Upstream positions each resize handle but gives it no size, background or cursor of its own --
   without the rules below every handle exists in the DOM but is 0x0, invisible and unclickable.
   ResizableNodeView builds its DOM at runtime, so it carries no scope id and needs `:deep(...)`
   the same as the ProseMirror table markup above. */
.rich-text__content :deep([data-resize-handle]) {
  width: 0.625rem;
  height: 0.625rem;
  background-color: var(--primary);
  border: 1px solid var(--background);
  border-radius: 9999px;
}
.rich-text__content :deep([data-resize-handle="top-left"]),
.rich-text__content :deep([data-resize-handle="bottom-right"]) { cursor: nwse-resize; }
.rich-text__content :deep([data-resize-handle="top-right"]),
.rich-text__content :deep([data-resize-handle="bottom-left"]) { cursor: nesw-resize; }
.rich-text__content :deep([data-resize-handle="top"]),
.rich-text__content :deep([data-resize-handle="bottom"]) { cursor: ns-resize; }
.rich-text__content :deep([data-resize-handle="left"]),
.rich-text__content :deep([data-resize-handle="right"]) { cursor: ew-resize; }

/* `.ProseMirror-selectednode` lands on the [data-resize-container] element, not on the wrapper or
   the <img>, because that container is what the node view returns as its `dom`.

   `width: fit-content` is load-bearing: upstream makes the container a block-level flex box, which
   otherwise fills the whole line, so the outline would draw around the full column while the
   handles stay hugging the image. It does nothing to clamp an oversized image -- what clamps one
   is Tailwind preflight's `img { max-width: 100% }`, and only that. */

.rich-text__content :deep([data-resize-container].ProseMirror-selectednode) {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}
.rich-text__content :deep([data-resize-container]) {
  width: fit-content;
  /* `prose`'s own image margin, re-applied here -- see the img rule below for why it has to move. */
  margin-top: 2em;
  margin-bottom: 2em;
}

/* `prose` puts a 2em vertical margin on every <img>, and the wrapper around it is a flex item, so
   that margin cannot collapse out -- it inflates the box the handles and the selection outline are
   both positioned against, leaving the handles off the image's own corners. Zeroing it here and
   re-applying it as the container's margin (outside its border box) keeps the same visual gap.
   The 2em above is hardcoded, not read from the plugin: switching this editor to `prose-sm` /
   `prose-lg` would change the plugin's value and silently drift from it. */
.rich-text__content :deep([data-resize-wrapper] img) {
  margin: 0;

  /* Upstream writes an inline pixel `height` on the <img> on every mousemove of a drag, which
     outranks Tailwind preflight's `height: auto` -- so once the width clamps at the column edge
     nothing keeps the height in proportion and the image stretches. `!important` is required:
     only an important stylesheet declaration can outrank an inline style. */
  height: auto !important;
}

/* Belt-and-braces alongside the onCreate setEditable() above, which is what actually removes the
   handle elements. `contenteditable` on the `.ProseMirror` root is prosemirror-view's own
   rendering of the editable flag, so this selector is exactly the read-only state. */
.rich-text__content :deep(.ProseMirror[contenteditable="false"] [data-resize-handle]) {
  display: none;
}
</style>
