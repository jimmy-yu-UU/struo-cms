<script setup lang="ts">
import { ref, watch, onBeforeUnmount, nextTick } from 'vue'
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
  // See blurActiveElementBeforeDialog's own comment (declared below) for the mechanism this
  // guards against. This dialog's own "cancel" close is @update:open below on a bare Dialog with
  // no v-model sugar -- onImageSelected (the "submit" path) instead sets imageDialogOpen directly
  // and never emits update:open at all, so the two never race over the same restore call.
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

// Shared by every dialog below that can open while the editor (or one of its own toolbar/menu
// controls) still holds DOM focus: openLinkDialog, onImageAction's editAlt branch, openImageDialog,
// and openTableSizeDialog. Verified this session in the installed reka-ui@2.10.3 source:
// DialogContentModal.js's useHideOthers is a `watch(() => unrefElement(target), ...)` whose
// callback calls the `aria-hidden` library's own hideOthers() directly, with no await inside it.
// So on the same reactive flush that flips a dialog's `open` true, hideOthers' own DOM write
// happens with nothing async in the way -- whatever element holds focus at that instant is still
// holding it when hideOthers runs. If that element is still a descendant of the app shell's own
// <main> (true on every path this guards in a real browser: a left-click on a toolbar/menu button
// focuses that button same as any native button click, and a right-click on an image never blurs
// the contenteditable the way a left-click elsewhere does -- see onContentContextMenu's own image
// branch above), Chrome logs an aria-hidden-on-an-element-whose-descendant-retains-focus warning
// against <main> before anything else moves focus off it.
//
// What DOES eventually move focus, for a modal reka dialog like the ones in this file, is NOT
// FocusScope's own `previouslyFocusedElement` restore -- verified this session that
// DialogContentModal.js's `onCloseAutoFocus` handler unconditionally calls `event.preventDefault()`
// on the event FocusScope dispatches for that, which is exactly what FocusScope's own cleanup
// checks before ever running its fallback (`if (!unmountEvent.defaultPrevented) focus(...)`) --
// so that fallback is dead code for every dialog in this file. The operative mechanism is
// DialogContentModal's OWN restore instead: `rootContext.triggerElement.value?.focus()`, where
// `triggerElement` is captured by DialogContentImpl's `onMounted`, guarded by
// `getActiveElement() !== document.body`. None of this file's dialogs use a reka `<DialogTrigger>`
// (they are all driven imperatively via `:open`), so that capture is the ONLY thing that ever sets
// `triggerElement` -- and because blurActiveElementBeforeDialog below empties
// document.activeElement to <body> before `open` ever flips true, that guard fails every time,
// leaving `triggerElement` permanently unset. Both of reka's own restore paths are therefore
// `undefined?.focus()` no-ops for every dialog here; elementToRefocusOnDialogCancel and
// refocusAfterDialogCancel below are the only focus restoration actually happening on a cancel.
//
// Blurring synchronously, before the open flag flips, empties document.activeElement down to
// <body> -- outside the subtree hideOthers is about to hide -- so there is nothing focused inside
// <main> left for that check to catch. elementToRefocusOnDialogCancel is a capture of whatever
// held focus right before that blur; refocusAfterDialogCancel is the matching restore. Every
// dialog-close handler in this file calls refocusAfterDialogCancel() only on a plain cancel --
// never on a submit/remove/insert/select close, which already runs its own deliberate
// editor.chain().focus() and clears elementToRefocusOnDialogCancel itself instead, so the restore
// is never fighting a focus target that was moved on purpose. settleLinkDialog above deliberately
// has no focus side effect of its own: it is also called from two "not reachable today" defensive
// spots (openLinkDialog's own top, and the onBeforeUnmount below) where a leftover, unconsumed
// capture could otherwise schedule a stale focus() call landing inside whatever dialog opens next.
// Only onLinkDialogOpenChange's own genuine-cancel branch pairs the two calls together.
let elementToRefocusOnDialogCancel: HTMLElement | null = null

function blurActiveElementBeforeDialog(): void {
  const active = document.activeElement
  // Guards against storing document.body itself as "the target to restore": nothing was
  // meaningfully focused in that case, and treating body as a real target would make the
  // eventual restore in refocusAfterDialogCancel() indistinguishable from a genuine one, when it
  // is actually restoring nothing.
  elementToRefocusOnDialogCancel = active instanceof HTMLElement && active !== document.body ? active : null
  if (active instanceof HTMLElement) active.blur()
}

// Deferred one tick, not called synchronously here -- verified this session, by running exactly
// this scenario and watching it fail without the deferral: reka's own FocusScope keeps its
// document-level focusin/focusout trap listeners attached (from a `watchEffect` keyed on its own
// `trapped` prop, itself driven by this same `open` flag) until Vue's reactivity actually flushes
// that prop change and tears them down -- which has not happened yet at the exact synchronous
// instant this function's caller runs, in the very same tick that flips `open` false. A
// `target.focus()` call made in that window is itself a native focusout from whatever was focused
// inside the dialog, and the still-attached trap's own handleFocusOut immediately calls
// `focus(lastFocusedElementRef.value)` -- refocusing back into the dialog and undoing the restore.
// Waiting for nextTick() lets that teardown finish first, so this focus() call is the last one to
// run.
//
// This calls the DOM's own `target.focus()` directly rather than routing through
// `editor.commands.focus()` for the editor-as-target case (the image-alt-dialog cancel path):
// whether a raw focus() call this way preserves the editor's own NodeSelection and its visible
// outline, versus editor.commands.focus() re-deriving/re-applying it, is a real question this
// session could not settle without a browser -- ProseMirror's own documentation says a plain DOM
// focus() does not itself alter `state.selection`, only the view's own `focused` flag and CSS
// state, but whether that is enough for the selection to still render as expected needs a live
// check, stated here rather than assumed either way.
function refocusAfterDialogCancel(): void {
  const target = elementToRefocusOnDialogCancel
  elementToRefocusOnDialogCancel = null
  if (!target) return
  void nextTick().then(() => {
    if (document.body.contains(target)) target.focus()
  })
}

function openLinkDialog(
  initial: { href: string; newTab: boolean; canRemove: boolean },
): Promise<{ href: string; newTab: boolean } | 'remove' | null> {
  // Not reachable today (only one link command can run at a time), but settle any still-pending
  // prior call with null rather than letting the assignment below silently overwrite
  // resolveLinkDialog and leave that earlier promise unresolved forever. Not paired with a
  // refocusAfterDialogCancel() call, unlike onLinkDialogOpenChange below -- see settleLinkDialog's
  // own comment for why the two are decoupled.
  settleLinkDialog(null)
  // Capture before hiding, not after: the bubble menu's own link button is one of the two things
  // that can hold focus when this runs (the toolbar's own link button is the other), and
  // bubbleMenuRef.hide() just below is what makes BubbleMenuView remove/hide that button --
  // capturing afterward would find it already gone and record whatever focus fell back to
  // instead of the real target.
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
  // richTextCommands.ts's own .then() chain runs editor.chain().focus() for this outcome --
  // clear the captured pre-dialog target so onLinkDialogOpenChange's own trailing
  // update:open(false) (RichTextLinkDialog's submit() emits both) finds nothing left to restore.
  elementToRefocusOnDialogCancel = null
}

function onLinkDialogRemove(): void {
  settleLinkDialog('remove')
  // Same reasoning as onLinkDialogSubmit above: the .then() chain's own unsetLink().focus() call
  // already refocuses deliberately for this outcome.
  elementToRefocusOnDialogCancel = null
}

// Fires for every close, including Cancel, Esc and an overlay click -- not only a plain cancel:
// submit/remove already resolved the promise AND cleared elementToRefocusOnDialogCancel
// themselves, by the time their own trailing update:open(false) reaches here, so settling with
// null again is a no-op and refocusAfterDialogCancel() below finds nothing left to restore. Only
// a genuine Cancel/Escape/overlay-click close reaches this with a live capture still in place.
function onLinkDialogOpenChange(open: boolean): void {
  linkDialogOpen.value = open
  if (!open) {
    settleLinkDialog(null)
    refocusAfterDialogCancel()
  }
}

// Not reachable today either (nothing in this repo unmounts a field mid-edit), but unmounting with
// the dialog still open would otherwise leave its promise pending forever -- resolving it with null
// here is the same "cancelled" outcome a plain Cancel click already produces. Not paired with
// refocusAfterDialogCancel() either, for the same reason noted on settleLinkDialog: the component
// is unmounting, so a focus() call scheduled a tick later would run against a torn-down tree.
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

// Set around a setEditable() call so its resulting onUpdate -> emitNormalized() (below) is a
// no-op -- see setEditableWithoutEmitting() further down for why setEditable() always triggers
// that chain and why a synchronous flag is enough to bracket it.
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
  // insertImage() above already ran its own editor.chain().focus() -- clear the captured
  // pre-dialog target rather than leaving it to leak into a later, unrelated dialog's own cancel.
  elementToRefocusOnDialogCancel = null
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
    // an editor drag an image's resize handles. `alwaysPreserveAspectRatio: true` locks every
    // drag to the image's own ratio rather than upstream's default of only doing so while Shift
    // is held: verified in ResizableNodeView.handleResize that `isShiftKeyPressed` is read at all
    // only when `preserveAspectRatio` (this option) is false, so once this is true there is no
    // per-drag override left to discover -- acceptable here because most editors of a
    // general-purpose CMS have no design background, and an accidental free-drag silently
    // stretches a published image with no visual cue that anything went wrong. minWidth keeps a
    // handle from shrinking the image into an unusably small target.
    //
    // directions requests all eight handles ResizableNodeViewDirection allows (verified in the
    // installed @tiptap/core source: that's the full union, and `directions` defaults to the four
    // corners alone when omitted) -- four corners plus the four edge midpoints the maintainer
    // asked for. Every one of the eight still resizes proportionally: alwaysPreserveAspectRatio
    // above is re-read from `this.preserveAspectRatio` on every mousemove, not branched on which
    // direction is active (confirmed in the same handleResize/calculateNewDimensions read), so an
    // edge handle locks the ratio exactly like a corner handle. That is coherent with the storage
    // contract: only `width` is ever persisted (see the addAttributes override below), so every
    // direction's drag round-trips through the same single number regardless of which axis the
    // user grabbed.
    //
    // Not coherent with a claim this comment used to make, since corrected here: no handle
    // "anchors" the opposite edge or corner in place. Confirmed by reading calculateNewDimensions
    // and applyAspectRatio (same source) directly: neither ever sets `left`/`top`/`right`/`bottom`
    // on the element itself, only `width`/`height` -- and the container's own
    // `justify-content: flex-start` default (never overridden anywhere in this file) means the
    // element's own top-left corner is what stays fixed for every direction. Dragging the left
    // handle inward, for instance, does not pin the right edge and grow leftward the way a design
    // tool's handles usually do -- it shrinks the same as every other handle, from the top-left.
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
    // A second, independent height defect this option does NOT address: ResizableNodeView's own
    // handleResize (verified in the installed @tiptap/core source) writes BOTH
    // `element.style.width` and `element.style.height` as inline pixel values on every mousemove,
    // unconditionally. Tailwind preflight's `img { max-width: 100%; height: auto }` does clamp the
    // rendered WIDTH once a drag passes the column's edge (max-width is a distinct property from
    // width, so it always applies) -- measured in the running admin, see the
    // `[data-resize-container]` rule below -- but the inline height always beats preflight's
    // stylesheet height for that same property, so nothing was left to clamp the height to
    // match -- the image stretched vertically past that point. The style block below forces
    // height back to auto on this node view's own img (needing !important to outrank the inline
    // style), which is a CSS-only fix and belongs there, not here.
    //
    // handleMouseUp's onCommit reads `this.element.offsetWidth` (same source), a live layout
    // measurement rather than the drag's raw unclamped delta. What that reads at the moment of a
    // drag past the column edge is settled, measured in the running admin this session: with the
    // inline width forced to 2000px inside the ~603px `prose` column, that same element's
    // offsetWidth reads 603 -- the clamped, on-column number, which is the desirable outcome
    // because it is also what publishing renders. A drag past the edge therefore stores the column
    // width, not the number the pointer travelled to.
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
  // (verified in @tiptap/core/src/Editor.ts's mount()) is scheduled via a `window.setTimeout(fn,
  // 0)` that is queued *before* EditorContent's nextTick job even exists (mount() runs
  // synchronously inside useEditor()'s own onMounted, earlier in the same tick that later
  // triggers EditorContent's reactive watchEffect) -- but a macrotask timer never runs until the
  // microtask queue, which is where that whole nextTick chain lives, is fully drained, so by the
  // time this callback actually fires, createNodeViews()'s replacement node views already exist
  // and are already listening.
  //
  // setEditable() (verified in the same Editor.ts) unconditionally emits 'update' even when the
  // value passed is one it already holds -- which is exactly the signal handleEditorUpdate needs,
  // but it is also indistinguishable from a real content change to onUpdate -> emitNormalized()
  // above, which has no guard of its own for a disabled field. Left unguarded, a read-only user
  // opening an item would get update:modelValue emitted at mount for every rich-text field, with
  // a normalized value that can legitimately differ from the stored one (e.g. withFileIds()
  // above adds data-file-id to any managed image that arrives without one) -- producing a false
  // unsaved-changes state the user never caused. suppressEmit (declared above emitNormalized())
  // is bracketed tightly around the call for exactly this reason: emit() (verified this session
  // in @tiptap/core/src/EventEmitter.ts) is a plain synchronous `callbacks.forEach(...)`, no
  // queueing, so the flag is still set for the entire synchronous
  // setEditable -> emit('update') -> onUpdate -> emitNormalized chain and is cleared the
  // instant setEditable() returns.
  onCreate: ({ editor: created }) => {
    if (!props.disabled) return
    suppressEmit = true
    created.setEditable(false)
    suppressEmit = false
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
// Same suppression as onCreate above and for the identical reason: setEditable() here also
// always emits 'update' (verified once, at the declaration of suppressEmit's rationale above),
// and a disabled<->enabled transition is exactly as real-world-reachable as a disabled mount --
// RBAC changes, or a form re-rendering the same field after a permission check settles -- so
// leaving this one call unguarded would just be the same defect reached through its other door.
// Same suppression as onCreate above and for the identical reason: setEditable() here also
// always emits 'update' (verified once, at the declaration of suppressEmit's rationale above),
// and a disabled<->enabled transition is exactly as real-world-reachable as a disabled mount --
// RBAC changes, or a form re-rendering the same field after a permission check settles -- so
// leaving this one call unguarded would just be the same defect reached through its other door.
watch(() => props.disabled, (d) => {
  const ed = editor.value
  if (!ed) return
  suppressEmit = true
  ed.setEditable(!d)
  suppressEmit = false
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
  // The chain above already refocuses the editor -- shared with the grid-picker path (which never
  // opens a dialog at all, so this is a harmless no-op there): clear the captured pre-dialog
  // target rather than leaving it to leak into a later, unrelated dialog's own cancel.
  elementToRefocusOnDialogCancel = null
}

// Opened by the table menu's "custom size…" entry; RichTextTableSizeDialog below reads it.
const sizeDialogOpen = ref(false)

function openTableSizeDialog(): void {
  // See blurActiveElementBeforeDialog's own comment (declared above, next to openLinkDialog) --
  // same mechanism, reached from a different click: the "Custom size..." entry inside
  // RichTextTableMenu's own reka Popover. Verified this session (see RichTextTableMenu.vue's own
  // onPopoverCloseAutoFocus comment for the source read): that Popover's own close-auto-focus
  // handler would otherwise refocus its `[data-cmd="table"]` trigger button -- a descendant of
  // <main> -- right after this dialog's own aria-hidden background applies, reintroducing the
  // warning; RichTextTableMenu.vue now suppresses that one restore itself. What this blur call
  // still does not fix: the target it captures here (whatever was focused inside the now-closing
  // popover) is removed from the DOM before any cancel of THIS dialog can run, so
  // refocusAfterDialogCancel()'s own document.body.contains(target) guard fails and that cancel
  // restore is a no-op -- accepted for now, not fixed, and worth a live check alongside the other
  // open items.
  blurActiveElementBeforeDialog()
  sizeDialogOpen.value = true
}

function onTableSizeDialogOpenChange(open: boolean): void {
  sizeDialogOpen.value = open
  if (!open) refocusAfterDialogCancel()
}

// Which action list RichTextContextMenu shows for the CURRENT right-click -- see that component's
// own `target` prop comment for why this is per-event state rather than something derived once.
const contextMenuTarget = ref<'table' | 'image' | null>(null)

const contentRoot = ref<HTMLElement | null>(null)

// Runs in the CAPTURE phase on a wrapper that is a STRICT ANCESTOR of reka's own trigger element
// (see the template below), and decides synchronously which menu the user gets. Checked in this
// order:
//
//   1. no editor yet: stopPropagation, target null -- nothing to route to.
//   2. an image: checked BEFORE the table, not after. `<td><img></td>` is legal content, and a
//      right-click landing on the <img> itself targets the image, not the cell it happens to sit
//      inside -- checking the table branch first would misroute that click to the table menu
//      instead, since isInEditorTable would also match (the image's ancestor cell). Confirmed by
//      actually swapping the two checks in this session and watching the table-cell-image test
//      below go red, not just by reasoning about it.
//   3. a table: existing behaviour, unchanged by this task.
//   4. neither: stopPropagation, target null -- native menu.
//
// Within each of the image/table branches:
//   - on a disabled (read-only) field: let the event continue on its own, doing nothing here.
//     RichTextContextMenu already forwards `disabled` to its own ContextMenuTrigger, which will
//     decline to open ours and fall back to the native menu, so there is nothing left for this
//     handler to add -- and a disabled/read-only editor should not have its selection moved at
//     all, which is why the selection-moving step is skipped too.
//   - on an enabled field: move the selection to the clicked node/cell, set `target`, then let the
//     event bubble on so reka opens ours.
//
// The stopPropagation branches (1 and 4) also matter for a reason unrelated to routing: reka's own
// menu never sees the event, so nothing calls preventDefault -- the browser's own menu (spellcheck,
// paste) appears untouched. This also stops ProseMirror's OWN contextmenu handler, which
// prosemirror-view registers on view.dom (a descendant of this wrapper) purely to force-flush a
// pending IME composition before the native menu opens (`handlers.contextmenu = view =>
// forceDOMFlush(view)`, itself `endComposition(view)`, in prosemirror-view/dist/index.js). The
// narrow, accepted consequence of suppressing that here is a possibly-stale native menu
// mid-composition -- nothing about table or image state.
//
// Two independent reasons to prefer stopPropagation over driving reka's own `disabled` prop, not
// one. Shape: `disabled` is a component-lifetime prop, while the decision here is per-event (which
// cell or image, if any, was clicked) -- a prop is the wrong vehicle for that regardless of timing.
// Timing: checked the installed reka-ui@2.10.3 source directly (ContextMenuTrigger.js) --
// `handleContextMenu` reads `disabled.value` synchronously as its very first statement, before its
// own `await nextTick()`. That value arrives as a PROP, forwarded through three component
// boundaries (this file's `disabled` -> RichTextContextMenu's own `disabled` prop -> the
// vendored ui/context-menu ContextMenuTrigger's `useForwardProps` -> reka's own `toRefs(props)`).
// Vue applies prop updates to a child component on its job queue, a microtask -- and DOM event
// dispatch from the capture phase to the bubble phase is synchronous, so no microtask can run in
// between. A ref flipped in this handler would still read stale at reka's guard, for the same
// event, every time. stopPropagation() sidesteps both problems at once. `target`, unlike
// `disabled`, does not face that same deadline -- see RichTextContextMenu.vue's own comment on its
// `target` prop for why a synchronous flip of it still arrives in time.
//
// The handler MUST sit on an element outside RichTextContextMenu, not on the element reka
// binds to. stopPropagation() does not stop other listeners on the SAME element -- only
// stopImmediatePropagation() does, and at-target listeners fire in registration order, which is
// not ours to control. From a strict ancestor, the capture listener always runs first and
// stopPropagation() reliably prevents the event from ever reaching reka.
function onContentContextMenu(e: MouseEvent): void {
  const root = contentRoot.value
  const ed = editor.value
  if (!root || !ed) { e.stopPropagation(); contextMenuTarget.value = null; return }

  const img = isEditorImage(e.target, root)
  if (img) {
    if (props.disabled) return
    // posAtDOM, not posAtCoords: unlike the table branch below, this handler already holds the
    // <img> element itself, so resolving its ProseMirror position needs no layout at all --
    // confirmed this session in the installed prosemirror-view@1.42.2 source
    // (EditorView.posAtDOM -> ViewDesc.posFromDOM/localPosFromDOM). Walking up from the <img> via
    // parentNode finds the image's own node-view desc on the first ancestor carrying one -- its
    // `dom` is the `[data-resize-container]` element (@tiptap/core's ResizableNodeView: `get dom()
    // { return this.container }`), with the <img> itself as that container's own first descendant
    // (ResizableNodeView.createWrapper() appends the element before attachHandles() appends any
    // handle siblings) -- so `offset: 0` walking up from a node with no previous sibling resolves
    // to the position right before the image node. NodeViewDesc.border is 0 for a leaf node (image
    // has no content), so that position is exactly `doc.nodeAt(pos) === <the image node>`, which is
    // what NodeSelection.create (and so setNodeSelection, its command form -- also verified in the
    // installed @tiptap/core source) needs.
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

// Two outcomes, one caller in this same file -- unlike RichTextLinkDialog's three-outcome,
// asynchronous-command promise protocol (see settleLinkDialog above), a plain ref pair
// (imageAltDialogOpen/imageAltDialogAlt below) is enough here: nothing outside this component
// needs to await the result, and there is no third "remove" outcome to distinguish from cancel.
const imageAltDialogOpen = ref(false)
const imageAltDialogAlt = ref('')

function onImageAction(action: ImageAction): void {
  const ed = editor.value
  if (!ed) return
  if (action === 'deleteImage') {
    // The context-menu handler above already moved the selection onto the image node (a
    // NodeSelection), so deleteSelection() removes exactly that node -- confirmed in the installed
    // @tiptap/core source that deleteSelection() deletes whatever range the CURRENT selection
    // covers, not a hardcoded assumption about node vs text selections.
    ed.chain().focus().deleteSelection().run()
    return
  }
  // editAlt: seed the dialog from the selected image's own current alt (getAttributes('image'),
  // also verified this session, reads whichever node within the current selection's range matches
  // the given type -- the NodeSelection set above makes that the image itself). alt legitimately
  // has no default value once created via setImage() with no alt argument, so this falls back to
  // an empty string rather than passing a non-string through to the dialog's `alt: string` prop.
  const attrs = ed.getAttributes('image')
  imageAltDialogAlt.value = typeof attrs.alt === 'string' ? attrs.alt : ''
  // See blurActiveElementBeforeDialog's own comment (declared above, next to openLinkDialog) for
  // why this call is here: the right-click that led to this action leaves the ProseMirror editor
  // itself focused (a right-click never blurs a contenteditable the way a left-click elsewhere
  // does), and this dialog's own aria-hidden background would otherwise be applied while that is
  // still true.
  blurActiveElementBeforeDialog()
  imageAltDialogOpen.value = true
}

function onImageAltDialogSubmit(alt: string): void {
  // updateAttributes('image', ...), not a fresh lookup of "the" image: it applies to whichever
  // node the CURRENT selection covers matching that type (verified this session in the installed
  // @tiptap/core source), which is still the NodeSelection the context-menu handler set, on the
  // same reasoning as deleteSelection() above -- the dialog offers no path back into the editor
  // that could move the selection while it is open.
  editor.value?.chain().focus().updateAttributes('image', { alt }).run()
  // The chain above already refocuses the editor -- clear the captured pre-dialog target (here,
  // the editor itself) rather than have onImageAltDialogOpenChange's own trailing update:open(false)
  // restore it a second time, or a later, unrelated dialog's own cancel inherit a stale one.
  elementToRefocusOnDialogCancel = null
}

// Unlike the link dialog, this one has no third "remove" outcome and no async settle protocol to
// share the close signal with -- but it does need to tell a submit-then-close apart from a plain
// cancel-close, which v-model:open sugar alone cannot do (RichTextImageAltDialog.submit() emits
// 'submit' then its own trailing 'update:open'(false), same shape as RichTextLinkDialog's submit;
// see that component's submit()). onImageAltDialogSubmit above always runs first and always clears
// elementToRefocusOnDialogCancel, so by the time this runs for a submit-driven close there is
// nothing left to restore -- refocusAfterDialogCancel() is only ever a real restore for a plain
// Cancel/Escape/overlay-click close.
function onImageAltDialogOpenChange(open: boolean): void {
  imageAltDialogOpen.value = open
  if (!open) refocusAfterDialogCancel()
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
    <!--
      Not v-model:open sugar: onTableSizeDialogOpenChange has to tell a submit-driven close (its
      own @insert already ran editor.chain().focus(), see onTableInsert) apart from a plain
      Cancel/Escape/overlay-click close, which needs refocusAfterDialogCancel() instead -- sugar's
      own `open = $event` has no way to run that extra step.
    -->
    <RichTextTableSizeDialog :open="sizeDialogOpen" @update:open="onTableSizeDialogOpenChange" @insert="onTableInsert" />
    <!--
      Not v-model:open sugar: a plain Cancel/Esc/overlay-click close still has to settle the
      pending openLinkDialog promise with null (which restores focus itself -- see
      settleLinkDialog), which sugar's own `open = $event` has no way to also do -- update:open is
      handled explicitly instead.
    -->
    <RichTextLinkDialog :open="linkDialogOpen" :href="linkDialogHref" :new-tab="linkDialogNewTab"
      :can-remove="linkDialogCanRemove" @update:open="onLinkDialogOpenChange"
      @submit="onLinkDialogSubmit" @remove="onLinkDialogRemove" />
    <!--
      Not v-model:open sugar here either, for the same reason as the table-size dialog above:
      onImageAltDialogOpenChange has to tell a submit-driven close apart from a plain cancel-close
      to know whether refocusAfterDialogCancel() should actually restore anything.
    -->
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
/* The `directions` resize option above now asks for the four edge midpoints too, alongside the
   four corners -- these two rules are the edge-direction cursors that pairing needs. */
.rich-text__content :deep([data-resize-handle="top"]),
.rich-text__content :deep([data-resize-handle="bottom"]) { cursor: ns-resize; }
.rich-text__content :deep([data-resize-handle="left"]),
.rich-text__content :deep([data-resize-handle="right"]) { cursor: ew-resize; }

/* Selection outline: confirmed by reading prosemirror-view@1.42.2's source this session, not
   assumed -- NodeViewDesc.create() (src/viewdesc.ts) sets `nodeDOM` to the exact DOM node a
   custom node view returns as its `dom` (ResizableNodeView's own `get dom()` returns
   `this.container`, the `[data-resize-container]` element), and CustomNodeViewDesc's
   selectNode()/deselectNode() (same file) fall through to the base ViewDesc implementation --
   since ResizableNodeView defines neither -- which toggles `.ProseMirror-selectednode` on that
   same `nodeDOM`. So the container, not the wrapper or the <img> itself, is what carries the
   class.

   Without the width rule just below, that outline would hug nothing: createContainer() (same
   source) sets `element.style.display = 'flex'` (this image is configured `inline: false`, so
   never 'inline-flex') on a plain <div>, and a block-level flex container with no other width
   constraint fills the available line width the same as any other block box -- so the outline
   would draw around the full line, with the image sitting at its left edge, while the handles
   (positioned against the wrapper just below, which sizes to its own content) stay hugging the
   image. `width: fit-content` is a plain CSS property this rule owns outright for THAT property --
   upstream's own inline style on this same element also sets `visibility`/`pointerEvents`
   (@tiptap/extension-image's addNodeView, to hide the node view until the image's onload fires),
   but never `width`, so there is no inline-style conflict on width specifically and no !important
   needed for the rule below. Shrinking the container to its single flex child (the wrapper) is
   what makes the outline and the handles agree on the same box.

   Measured live this session, in the running admin itself -- not a static reconstruction, which is
   what two earlier rounds of this comment argued past each other over. Headless Chromium
   (@playwright/test) against the dev server, a real article's image inside the real ~603px
   `prose` column, injecting overrides and reading getComputedStyle back to confirm each one
   actually applied before trusting the numbers:

     - `width: fit-content` above IS load-bearing, and this is what it does: with a 200px image the
       container measures 200px and the selection outline hugs it; overridden to `width: auto` the
       same container measures the full 603px column with the image at its left edge. That is the
       outline/handle agreement described above, and nothing to do with clamping.
     - What clamps an oversized image is Tailwind preflight's `img { max-width: 100% }`, and only
       that. Overridden to `max-width: none` with everything else untouched, a 2000px inline width
       renders at 2000px, and an image with a 3000px INTRINSIC width and no inline width at all
       renders at 3000px. Left alone, both render at 603px.
     - Two declarations this file used to carry, `max-width: 100%` on this container and
       `min-width: 0` on the wrapper, measured inert and are gone. Neutralized singly and together,
       against both the 2000px-inline-width and the 3000px-intrinsic-width cases -- the latter being
       exactly the flex automatic-minimum-size / fit-content cyclic-percentage scenario each was
       written against -- every box still measured 603px. They were not kept as insurance either,
       because in the one configuration that does overflow (preflight neutralized) neither of them
       prevents the overflow: with both still in place the image itself rendered at 2000px and
       3000px respectively, and the only thing the container's own cap changed was whether this box
       stayed at 603px while its image spilled out of it, or grew with it. There is no failure mode
       left for them to insure against.
   */

.rich-text__content :deep([data-resize-container].ProseMirror-selectednode) {
  outline: 2px solid var(--primary);
  outline-offset: 2px;
}
.rich-text__content :deep([data-resize-container]) {
  width: fit-content;
  /* Margin, not the outline/hugging rules above: see the comment on the img margin rule just
     below for why this box (not the wrapper, and not the img itself) is where `prose`'s own
     vertical image margin has to be re-applied. */
  margin-top: 2em;
  margin-bottom: 2em;
}

/* The wrapper (createWrapper(), same source) is a plain `display: block` div holding only the
   <img> and its absolutely-positioned handles -- but it is also a flex ITEM of the container just
   above, and a flex item's content is explicitly specified (CSS Flexible Box Layout) to form a new
   formatting context for its own children, so a child's own vertical margins do not collapse
   through it the way they normally would through an ordinary block parent. `prose` (this editor's
   own typography class, applied above via editorProps) puts a vertical margin directly on every
   `<img>` -- confirmed by reading the installed @tailwindcss/typography@0.5.20 source
   (styles.js's `base` modifier, which is what an unmodified `prose` class resolves to): `margin-top`
   and `margin-bottom` both `2em`. Uncollapsed, that margin sits inside the wrapper's own rendered
   box, so the wrapper (and, through it, the fit-content container above) is taller than the image
   by that margin on both edges -- which is exactly why the resize handles, positioned with
   top:0/bottom:0 against the wrapper's own edges, previously sat on a box visibly taller than the
   image rather than on the image's own corners.

   Zeroing that margin here and re-adding it on the container's own margin (declared on the rule
   above, since it is a real block box in document flow whose margin sits OUTSIDE its border box
   and so never inflates what the outline above measures) keeps the same visual gap above and
   below an image that `prose` would otherwise have provided, without inflating the box the
   outline and handles both key off. The `2em` above is a hardcoded approximation of that same
   `base` modifier value, not a read of it: this editor's `class="prose dark:prose-invert"` (no
   size suffix) always resolves to that modifier today, so the two happen to agree, but this rule
   does not track the plugin's own theme value and would silently drift if a future edit switched
   to `prose-sm`/`prose-lg`/etc. */
.rich-text__content :deep([data-resize-wrapper] img) {
  margin: 0;

  /* Second, independent defect in the same drag path (see the `directions` comment on the Image
     extension's own `resize` option above for the full mechanism): ResizableNodeView.handleResize
     always writes an inline `height` in pixels on every mousemove, and that inline value always
     outranks Tailwind preflight's stylesheet `height: auto` for the same property -- so once a
     drag pushed the inline width past what `max-width: 100%` lets the image actually render at,
     nothing was left to keep the rendered height in proportion, and the image stretched.
     `!important` here is required, not decorative: only `!important` on a stylesheet rule can
     outrank an inline style for the same property. Restoring `height: auto` makes the browser
     derive the rendered height from the image's own natural aspect ratio and whatever width it
     actually rendered at (clamped or not) -- the same ratio ResizableNodeView itself measured at
     mount (applyInitialSize() reads this same element's offsetWidth/offsetHeight), so this does
     not fight alwaysPreserveAspectRatio's own math, only the one place upstream still applies a
     size as an inline style unconditionally. */
  height: auto !important;
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
