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
import { Dialog, DialogScrollContent, DialogHeader, DialogTitle, DialogDescription } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import MediaGrid from '../media/MediaGrid.vue'
import RichTextBubbleMenu from './RichTextBubbleMenu.vue'
import RichTextCommandButton from './RichTextCommandButton.vue'
import RichTextColorMenu from './RichTextColorMenu.vue'
import RichTextHeadingMenu from './RichTextHeadingMenu.vue'
import RichTextLinkDialog from './RichTextLinkDialog.vue'
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

function openLinkDialog(
  initial: { href: string; newTab: boolean; canRemove: boolean },
): Promise<{ href: string; newTab: boolean } | 'remove' | null> {
  // Not reachable today (only one link command can run at a time), but settle any still-pending
  // prior call with null rather than letting the assignment below silently overwrite
  // resolveLinkDialog and leave that earlier promise unresolved forever.
  settleLinkDialog(null)
  // Force the bubble menu away first (a no-op if it was never showing -- BubbleMenuView.hide()
  // guards on its own isVisible): opening this dialog from its own link button is one of the two
  // entry points sharing this function, and the dialog's autofocus stealing DOM focus from the
  // editor would otherwise leave that menu lingering beside it (RT-5's leftover; see
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
}

function onLinkDialogRemove(): void {
  settleLinkDialog('remove')
}

// Fires for every close, including Cancel, Esc and an overlay click -- not only a plain cancel:
// submit/remove already resolved (and cleared) the promise by the time their own trailing
// update:open(false) reaches here, so settling with null again is the no-op described above.
function onLinkDialogOpenChange(open: boolean): void {
  linkDialogOpen.value = open
  if (!open) settleLinkDialog(null)
}

// Not reachable today either (nothing in this repo unmounts a field mid-edit), but unmounting with
// the dialog still open would otherwise leave its promise pending forever -- resolving it with null
// here is the same "cancelled" outcome a plain Cancel click already produces.
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
    // `resize.enabled` swaps in @tiptap/core's ResizableNodeView (verified in its installed
    // source: addNodeView() returns null unless this is true -- it also returns null when
    // `typeof document === 'undefined'`, an SSR guard that doesn't apply to this SPA), which lets
    // an editor drag an image's corner handles. `alwaysPreserveAspectRatio: true` locks every
    // drag to the image's own ratio rather than upstream's default of only doing so while Shift
    // is held: verified in ResizableNodeView.handleResize that `isShiftKeyPressed` is read at all
    // only when `preserveAspectRatio` (this option) is false, so once this is true there is no
    // per-drag override left to discover -- acceptable here because most editors of a
    // general-purpose CMS have no design background, and an accidental free-drag silently
    // stretches a published image with no visual cue that anything went wrong. minWidth keeps a
    // handle from shrinking the image into an unusably small target.
    //
    // height is never rendered into the serialized HTML, via the addAttributes override below.
    // This is NOT a duplicate of the backend's sanitizer: Task 1 already strips height there
    // unconditionally (it was never in the allowlist), so that alone would already keep a
    // stray height out of anything actually persisted. What this guards is local to the editor:
    // ResizableNodeView's onCommit (upstream, in @tiptap/extension-image) always writes both
    // width and height as node attributes after a drag, so without this override getHTML() would
    // carry a height that the stored value never will. The watch(() => props.modelValue) below
    // compares getHTML()'s output to the stored prop directly -- if the two disagree only because
    // of a height neither side actually wants, that watch fires setContent() on every external
    // update and resets the cursor for no reason.
    //
    // Known upstream defect, not introduced here: ResizableNodeView's constructor registers
    // `editor.on('update', this.handleEditorUpdate.bind(this))`, and its destroy() calls
    // `editor.off('update', this.handleEditorUpdate.bind(this))` -- but `.bind()` returns a new
    // function object each time it's called, so the listener destroy() removes is never the one
    // the constructor added. Every image node view this editor ever creates leaks one 'update'
    // listener on the editor for the editor's own lifetime. We cannot fix this from an extension
    // config; recorded here so it isn't mistaken for something introduced by this change.
    Image.extend({
      addAttributes() {
        // Verified in the installed @tiptap/extension-image@3.30.2 source: the parent's
        // addAttributes() returns { src: {...}, alt: {...}, title: {...}, width: { default:
        // null }, height: { default: null } } -- a plain name-to-config map, one entry per
        // attribute. Spreading it forward and then replacing only `height` keeps src/alt/
        // title/width exactly as upstream defines them.
        return {
          ...this.parent?.(),
          height: { default: null, rendered: false },
        }
      },
    }).configure({
      inline: false,
      resize: { enabled: true, minWidth: 40, alwaysPreserveAspectRatio: true },
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
  // Closes a gap the watch(() => props.disabled) below cannot: that watch only fires on a
  // *change*, so a field that mounts already disabled never runs it. Verified this session in
  // @tiptap/core/src/lib/ResizableNodeView.ts -- every image node view's constructor calls
  // attachHandles() unconditionally and starts `lastEditableState` as `undefined`, and the only
  // thing that ever calls removeHandles() is handleEditorUpdate(), which only runs from the
  // editor's own 'update' event. So a disabled-from-the-start field would otherwise get live,
  // draggable handles that nothing ever tells to remove themselves -- and handleResizeStart has
  // no isEditable guard of its own, so dragging one would emit update:modelValue from a
  // read-only field. onCreate, not a plain Vue onMounted, is load-bearing here: verified this
  // session that @tiptap/vue-3's EditorContent component reparents the editor's DOM into its own
  // root element and then calls editor.createNodeViews() (-> view.setProps({ nodeViews })) inside
  // its own nextTick() -- which recreates every node view from scratch, wiping out any earlier
  // setEditable() call made from a plain onMounted before that reparenting has run. onCreate
  // (verified in @tiptap/core/src/Editor.ts's mount()) fires from a `window.setTimeout(fn, 0)`
  // queued after that reparenting starts, and macrotask timers never run until the microtask
  // queue -- which is where Vue's nextTick chain lives -- is fully drained, so by the time this
  // callback runs, createNodeViews()'s replacement node views already exist and are listening.
  // setEditable() (verified in the same Editor.ts) unconditionally emits 'update' even when the
  // value passed is one it already holds, which is exactly the signal handleEditorUpdate needs.
  onCreate: ({ editor: created }) => {
    if (props.disabled) created.setEditable(false)
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
  // otherwise apply to wherever the caret happens to be. posAtCoords needs real layout to resolve
  // accurate coordinates, and jsdom lays nothing out -- this line needs a live browser check, not a
  // jsdom test, to confirm the resolved position really does land in the cell under the cursor.
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
        :editor="editor" :disabled="disabled" @run="runCommand(cmd)" />
      <RichTextHeadingMenu :disabled="disabled" :active-level="activeHeadingLevel()"
        @select="onHeadingSelect" />
      <RichTextCommandButton v-for="cmd in TOOLBAR_BEFORE_COLOR" :key="cmd.id" :command="cmd"
        :editor="editor" :disabled="disabled" @run="runCommand(cmd)" />
      <RichTextColorMenu :disabled="disabled"
        :active-color="(editor.getAttributes('textStyle').color as string | undefined) ?? null"
        @pick="(c: string) => editor!.chain().focus().setColor(c).run()"
        @clear="editor!.chain().focus().unsetColor().run()" />
      <RichTextTableMenu :disabled="disabled" @insert="onTableInsert" @custom-size="sizeDialogOpen = true" />
      <RichTextCommandButton v-for="cmd in TOOLBAR_AFTER_TABLE" :key="cmd.id" :command="cmd"
        :editor="editor" :disabled="disabled" @run="runCommand(cmd)" />
    </div>
    <div ref="contentRoot" @contextmenu.capture="onContentContextMenu">
      <RichTextTableContextMenu :disabled="disabled" @action="onTableAction">
        <EditorContent class="rich-text__content min-h-32 p-2.5" :editor="editor"
          @click.self="editor?.chain().focus().run()" />
      </RichTextTableContextMenu>
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
    <Dialog v-model:open="imageDialogOpen">
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
    <RichTextTableSizeDialog v-model:open="sizeDialogOpen" @insert="onTableInsert" />
    <!--
      Not v-model:open sugar (unlike the table-size dialog above): a plain Cancel/Esc/overlay-click
      close still has to settle the pending openLinkDialog promise with null, which sugar's own
      `open = $event` has no way to also do -- update:open is handled explicitly instead.
    -->
    <RichTextLinkDialog :open="linkDialogOpen" :href="linkDialogHref" :new-tab="linkDialogNewTab"
      :can-remove="linkDialogCanRemove" @update:open="onLinkDialogOpenChange"
      @submit="onLinkDialogSubmit" @remove="onLinkDialogRemove" />
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

/* @tiptap/core's ResizableNodeView (wired up above on the Image extension) builds its own DOM at
   runtime via document.createElement, the same as the ProseMirror table markup styled above --
   it is neither Vue-rendered nor scope-id-bearing either, for the same reason given in this
   block's opening comment, so every rule below also goes through `:deep(...)`.

   Handles: createHandle() (upstream, @tiptap/core/src/lib/ResizableNodeView.ts) sets
   `position: absolute` and the `data-resize-handle` attribute, and attachHandles() additionally
   calls positionHandle() to place each one via `top`/`bottom`/`left`/`right`. Neither sets any
   size, background, or cursor (`classNames.handle` defaults to `''`, so there is no class to
   hook either). Without the rules below every handle is positioned but 0x0, invisible, and
   unclickable. */
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
/* No rules for the 'top'/'bottom'/'left'/'right' edge directions: the `directions` resize option
   is never passed above, so ResizableNodeView's own default (the four corners only) is what's
   ever attached -- confirmed by reading its `directions` field default in the same source file.
   Adding cursor rules for directions this config can't produce would be dead weight. */

/* Selection outline: confirmed by reading prosemirror-view@1.42.2's source this session, not
   assumed -- NodeViewDesc.create() (src/viewdesc.ts) sets `nodeDOM` to the exact DOM node a
   custom node view returns as its `dom` (ResizableNodeView's own `get dom()` returns
   `this.container`, the `[data-resize-container]` element), and CustomNodeViewDesc's
   selectNode()/deselectNode() (same file) fall through to the base ViewDesc implementation --
   since ResizableNodeView defines neither -- which toggles `.ProseMirror-selectednode` on that
   same `nodeDOM`. So the container, not the wrapper or the <img> itself, is what carries the
   class. */
.rich-text__content :deep([data-resize-container].ProseMirror-selectednode) {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}

/* Defense-in-depth alongside the editor's own onCreate option above (see the comment on it, next
   to onUpdate) -- that fix removes the handle elements from the DOM outright when a field mounts
   already disabled; this rule doesn't replace it, since CSS alone can never make a DOM node stop
   existing. This rule exists for any node view this session's reasoning didn't anticipate --
   e.g. a future edit that inserts content into a disabled field by some path other than the UI,
   or an upstream version where removeHandles() doesn't run when expected. Confirmed this session
   (not assumed) that the attribute this keys off is real: prosemirror-view@1.42.2's
   computeDocDeco() (src/index.ts) sets `attrs.contenteditable = String(view.editable)` on the
   decoration that becomes the `.ProseMirror` root element's own attributes -- so
   `.ProseMirror[contenteditable="false"]` is exactly the read-only state, not a guess. */
.rich-text__content :deep(.ProseMirror[contenteditable="false"] [data-resize-handle]) {
  display: none;
}
</style>
