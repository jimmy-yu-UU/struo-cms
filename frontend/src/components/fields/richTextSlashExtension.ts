import { Extension, VueRenderer, type Editor } from '@tiptap/vue-3'
import Suggestion, { exitSuggestion } from '@tiptap/suggestion'
import RichTextSlashMenu from './RichTextSlashMenu.vue'
import { buildSlashItems, filterSlashItems, type RichTextSlashItem } from './richTextSlashCommands'
import type { RichTextCommandContext } from './richTextCommands'

export interface RichTextSlashOptions {
  context: RichTextCommandContext
  t: (key: string) => string
  idPrefix: string
}

// Only the image item reaches into the command context, so an unconfigured extension would
// otherwise give a fork a menu where every other item works and the image item deletes the typed
// query and then does nothing at all. No type can require the option -- Extension.configure() takes
// a Partial -- so this default is the enforcement, and it names what is missing.
//
// openLinkDialog is here for the interface only: `link` is a group: 'inline' command and this menu
// builds headings, 'block', 'insert' and table, so no slash item can reach it.
const UNCONFIGURED_CONTEXT: RichTextCommandContext = {
  openImageDialog: () => {
    throw new Error('RichTextSlashExtension: configure({ context }) before using the image item')
  },
  openLinkDialog: () => {
    throw new Error('RichTextSlashExtension: configure({ context }) before opening the link dialog')
  },
}

export const RichTextSlashExtension = Extension.create<RichTextSlashOptions>({
  name: 'richTextSlash',

  addOptions() {
    return { context: UNCONFIGURED_CONTEXT, t: (key: string) => key, idPrefix: 'richtext-slash' }
  },

  addProseMirrorPlugins() {
    const { context, t, idPrefix } = this.options
    const editor = this.editor as Editor

    // The same string RichTextSlashMenu.vue renders as each option's own id, rebuilt here rather
    // than shared through a helper. The duplication is the contract: this formula and the
    // component's have to stay identical, or aria-activedescendant points at nothing.
    const optionId = (item: RichTextSlashItem): string => `${idPrefix}-${item.id}`
    // The listbox's own id. Free of collision only because no slash item is called 'listbox';
    // adding one would make this exact string an option id too.
    const menuId = `${idPrefix}-listbox`

    // Escape means "no", and upstream makes that stick: exiting records the range in its own
    // dismissedRange, and typing more of the same query does not reopen the menu. A blur does not
    // mean "no" -- Tab, a click into another field, and in Chrome the window itself losing focus all
    // fire one -- so a blur-driven exit must not leave the query dismissed.
    //
    // Two flags rather than one, because the answer has to be paired to a particular dismissal and
    // not merely set: onEditorBlur arms `exitIsFromBlur` for the single dispatch it is about to
    // make, and onExit hands that to `lastExitWasBlur` and clears it. The pairing is what makes this
    // self-healing -- onExit re-derives on EVERY deactivation, so a stale true cannot survive to
    // defeat a later Escape.
    //
    // It rests on onExit running synchronously inside the exit dispatch. Upstream's plugin view is
    // an async function, but its "stopped" branch has no await ahead of it, so the transaction that
    // records the dismissal and the onExit that labels it are one synchronous turn. If upstream ever
    // puts an await before that branch the label lands a turn late and a blur dismissal is treated
    // as an Escape one -- which is the behaviour before this existed, not a corrupt state.
    let exitIsFromBlur = false
    let lastExitWasBlur = false

    return [
      Suggestion<RichTextSlashItem, RichTextSlashItem>({
        editor,
        char: '/',
        // allowedPrefixes is deliberately left at its upstream default of [' ']: the rule that a
        // slash triggers only where a space or nothing at all precedes it is upstream's, not ours,
        // and it is what keeps "and/or" and URL paths from opening the menu. "Nothing at all" is
        // narrower than it sounds, and that is the boundary we inherit rather than choose --
        // upstream measures the prefix inside the single text node before the caret
        // (findSuggestionMatch reads $position.nodeBefore.text) and not the block, so a slash typed
        // right after a NON-inclusive mark ends, as in "<a>read more</a>/", sits at offset 0 of a
        // fresh text node, has no prefix character to reject, and does open the menu. An inclusive
        // mark does not hit this: ProseMirror only drops a mark at a boundary when its own spec sets
        // inclusive: false (read in prosemirror-model's ResolvedPos#marks(), which is what decides
        // which marks a freshly typed character picks up), and neither Bold nor any other mark used
        // here overrides that, so it falls back to the true default -- "<strong>bold</strong>/" stays
        // part of the same, still-bold text node and does NOT open the menu on its own. Link is the
        // mark that actually reaches this repo's boundary case, because it is configured
        // non-inclusive (autolink: false, and extension-link's own inclusive() returns that
        // option verbatim) -- see RichTextInput.vue's Link.configure(). startOfLine stays false.
        //
        // No isEditable clause here: upstream's apply() wraps its whole match-and-allow block in
        // one, so this is unreachable for a read-only editor and the check could never be false.
        allow: ({ state, range }) =>
          state.doc.resolve(range.from).parent.type.name !== 'codeBlock',
        // Clearing the dismissal here brings the menu back when the caret RETURNS to the dismissed
        // query, not only on the next keystroke, because upstream re-evaluates on any transaction
        // and a selection change is one. That is what a query which was never dismissed already
        // does, so it is the consistent outcome rather than an accident of this hook.
        shouldResetDismissed: () => lastExitWasBlur,
        // buildSlashItems runs per query rather than once when the extension is built: an
        // extension is constructed a single time with the editor, while the UI locale changes
        // during its lifetime and every label here comes from t().
        items: ({ query }) => filterSlashItems(buildSlashItems(t), query),
        // The callback's own `editor` argument is ignored on purpose: it is this same instance, but
        // typed as @tiptap/core's Editor, which a slash item's run() -- declared against
        // @tiptap/vue-3's -- does not accept.
        command: ({ range, props }) => {
          // deleteRange strictly before props.run. `range` describes where the typed "/query" sat
          // when the menu last updated; running the item first inserts a node or transforms the
          // block under the caret, and those positions then no longer describe that text -- the
          // delete would take out part of what was just inserted.
          editor.chain().focus().deleteRange(range).run()
          props.run(editor, context)
        },
        render: () => {
          let renderer: VueRenderer | null = null
          let unmount: (() => void) | null = null
          let menuEl: HTMLElement | null = null
          let items: RichTextSlashItem[] = []
          let selected = 0
          let commit: ((item: RichTextSlashItem) => void) | null = null

          // Hover is trusted only once the pointer has actually moved, and every keyboard move
          // withdraws that trust again. Without it a cursor left resting over the list hijacks the
          // arrow keys: past the fold the rows scroll under a stationary pointer, the browser
          // re-evaluates hover, and mouseenter fires on whichever row slid underneath -- which
          // walks the selection backwards and leaves the last rows unreachable from the keyboard.
          //
          // The flag lives here rather than in the component because this is the only side that
          // sees both inputs: keys are delivered to the editor, never to the menu. The component
          // stays stateless and keeps emitting hover for every mouseenter.
          let pointerMoved = false
          const onPointerMove = (): void => { pointerMoved = true }

          // A blur does not tear down the suggestion plugin's state -- that is recomputed only
          // inside apply(), and a blur dispatches no transaction -- so leaving the editor by any
          // route other than a click outside would strand the menu on screen, still owned by an
          // editable that no longer has focus. Safe against the mouse path because the menu's ROOT
          // carries @mousedown.prevent (RichTextSlashMenu.vue -- the rows themselves do not), so
          // choosing an item, or slipping onto the padding around one, never blurs the editor at all.
          const onEditorBlur = (): void => {
            exitIsFromBlur = true
            exitSuggestion(editor.view)
            exitIsFromBlur = false
          }

          function sync(): void {
            renderer?.updateProps({ items, selectedIndex: selected })
            const active = items[selected]
            if (!active) {
              editor.view.dom.removeAttribute('aria-activedescendant')
              return
            }
            const id = optionId(active)
            editor.view.dom.setAttribute('aria-activedescendant', id)
            const option = document.getElementById(id)
            if (option) scrollSelectedIntoView(option)
          }

          // The menu is height-capped and scrolls, so without this the selection walks out of
          // sight a few rows down -- on the editor's only keyboard-driven surface.
          //
          // Emphatically NOT Element.scrollIntoView. That walks every scrollable ancestor up to the
          // document, and the first sync() of an open runs while upstream has appended the menu to
          // <body> but floating-ui has not positioned it yet -- so the option is still in normal
          // flow at the end of the document, and scrolling it into view throws the page to its
          // maximum, taking the caret, the editor and the menu off-screen. Deferring the call does
          // not fix that; a later scrollIntoView can still reach the document. The menu is its own
          // max-h-72 overflow-y-auto scroll container, so that is the only thing scrolled here.
          //
          // Measured against the list's content box and adjusted RELATIVELY, which assumes no
          // coordinate space of its own and so is correct both before and after floating-ui
          // positions the menu -- unlike offsetTop, whose frame of reference changes at that moment.
          function scrollSelectedIntoView(option: HTMLElement): void {
            const list = menuEl
            if (!list) return
            const optionBox = option.getBoundingClientRect()
            const listBox = list.getBoundingClientRect()
            const visibleTop = listBox.top + list.clientTop
            const visibleBottom = visibleTop + list.clientHeight
            if (optionBox.top < visibleTop) list.scrollTop -= visibleTop - optionBox.top
            else if (optionBox.bottom > visibleBottom) list.scrollTop += optionBox.bottom - visibleBottom
          }

          function pick(index: number): void {
            const item = items[index]
            if (item) commit?.(item)
          }

          return {
            onStart: (props) => {
              // Upstream hands onStart `initialItems ?? []`, never the result of items() -- that
              // arrives in the onUpdate dispatched straight afterwards. So the menu opens on its
              // empty state by design, and every list a user sees comes from onUpdate.
              items = [...props.items]
              selected = 0
              commit = props.command
              // Narrow plain data only. VueRenderer's constructor does `this.props =
              // reactive(props)` and never markRaws it, so whatever is handed over here is deeply
              // proxied: passing SuggestionProps wholesale would wrap the editor, a ProseMirror
              // range and a live DOM node in proxies.
              renderer = new VueRenderer(RichTextSlashMenu, {
                editor,
                props: {
                  items,
                  selectedIndex: selected,
                  idPrefix,
                  onSelect: pick,
                  onHover: (i: number) => {
                    if (!pointerMoved) return
                    selected = i
                    sync()
                  },
                },
              })
              const el = renderer.element as HTMLElement | null
              if (el) {
                el.id = menuId
                menuEl = el
                pointerMoved = false
                document.addEventListener('pointermove', onPointerMove, { passive: true })
                editor.view.dom.addEventListener('blur', onEditorBlur)
                unmount = props.mount(el)
                // aria-activedescendant alone cannot resolve from here: props.mount() appends the
                // menu into the configured container, which defaults to document.body
                // (resolveContainer in @tiptap/suggestion's dist), so the option it names is not a
                // descendant of the editable carrying the attribute. ARIA resolves such a
                // reference only when the target is a descendant of the element with focus or of
                // an element that element owns, and aria-owns supplies the second case. The pair
                // goes on and comes off together -- an aria-owns left behind once the menu has
                // unmounted is a dangling reference.
                editor.view.dom.setAttribute('aria-owns', menuId)
              }
              sync()
            },

            onUpdate: (props) => {
              items = [...props.items]
              commit = props.command
              if (selected >= items.length) selected = 0
              sync()
            },

            // Escape is absent on purpose. @tiptap/suggestion's own handleKeyDown intercepts it
            // before this return value is consulted and dispatches the exit transaction itself, so
            // a branch here could only dispatch a second, redundant one. That is also why the
            // empty-list early return below cannot swallow it.
            onKeyDown: ({ event }) => {
              if (items.length === 0) return false
              if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                const step = event.key === 'ArrowDown' ? 1 : items.length - 1
                selected = (selected + step) % items.length
                // Disarmed BEFORE sync(), not after, and the order is load-bearing: sync() is what
                // scrolls the list, and a scroll makes the browser re-evaluate hover under a pointer
                // that never moved. Disarming first means the mouseenter that recomputation fires
                // arrives with hover already untrusted, instead of being honoured against whatever
                // movement the user made before this key press.
                pointerMoved = false
                sync()
                return true
              }
              if (event.key === 'Enter') { pick(selected); return true }
              // Tab among them: letting it through is what keeps the field's place in the form's
              // tab order.
              return false
            },

            onExit: () => {
              lastExitWasBlur = exitIsFromBlur
              exitIsFromBlur = false
              editor.view.dom.removeAttribute('aria-activedescendant')
              editor.view.dom.removeAttribute('aria-owns')
              document.removeEventListener('pointermove', onPointerMove)
              editor.view.dom.removeEventListener('blur', onEditorBlur)
              menuEl = null
              // Both calls are needed even though either one on its own already takes the menu out
              // of the document. props.mount()'s returned function is the only thing that stops
              // floating-ui's autoUpdate loop AND removes the capture-phase document pointerdown
              // listener upstream registers for dismissOnOutsideClick; destroy() is the only thing
              // that releases the Vue component instance.
              //
              // Those two teardowns live in one closure upstream, and only the listener half can be
              // asserted from jsdom -- floating-ui's loop is not observable there. So the autoUpdate
              // teardown is pinned by coupling, not by an assertion of its own: if upstream ever
              // splits the returned function in two, that half stops being covered.
              unmount?.(); unmount = null
              renderer?.destroy(); renderer = null
              commit = null
              items = []
            },
          }
        },
      }),
    ]
  },
})
