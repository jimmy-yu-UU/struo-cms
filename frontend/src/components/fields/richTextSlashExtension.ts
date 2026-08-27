import { Extension, VueRenderer, type Editor } from '@tiptap/vue-3'
import Suggestion from '@tiptap/suggestion'
import RichTextSlashMenu from './RichTextSlashMenu.vue'
import { buildSlashItems, filterSlashItems, type RichTextSlashItem } from './richTextSlashCommands'
import type { RichTextCommandContext } from './richTextCommands'

export interface RichTextSlashOptions {
  context: RichTextCommandContext | null
  t: (key: string) => string
  idPrefix: string
}

export const RichTextSlashExtension = Extension.create<RichTextSlashOptions>({
  name: 'richTextSlash',

  addOptions() {
    return { context: null, t: (key: string) => key, idPrefix: 'richtext-slash' }
  },

  addProseMirrorPlugins() {
    const { context, t, idPrefix } = this.options
    const editor = this.editor as Editor

    // The same string RichTextSlashMenu.vue renders as each option's own id, rebuilt here rather
    // than shared through a helper. The duplication is the contract, and each side pins it: a
    // change to one alone fails there instead of quietly producing an aria-activedescendant that
    // points at nothing.
    const optionId = (item: RichTextSlashItem): string => `${idPrefix}-${item.id}`
    // The listbox's own id. Free of collision only because no slash item is called 'listbox';
    // adding one would make this exact string an option id too.
    const menuId = `${idPrefix}-listbox`

    return [
      Suggestion<RichTextSlashItem, RichTextSlashItem>({
        editor,
        char: '/',
        // allowedPrefixes is deliberately left at its upstream default of [' ']: the rule that a
        // slash triggers only at the start of a line or after a space is upstream's, not ours, and
        // it is what keeps "and/or" and URL paths from opening the menu. One boundary comes with
        // it that we inherit rather than choose -- upstream measures that prefix inside the single
        // text node before the caret (findSuggestionMatch reads $position.nodeBefore.text), so a
        // slash typed right after a mark ends, as in "<strong>bold</strong>/", sits at offset 0 of
        // a fresh text node, has no prefix character to reject, and does open the menu.
        allow: ({ editor: ed, state, range }) =>
          ed.isEditable && state.doc.resolve(range.from).parent.type.name !== 'codeBlock',
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
          if (context) props.run(editor, context)
        },
        render: () => {
          let renderer: VueRenderer | null = null
          let unmount: (() => void) | null = null
          let items: RichTextSlashItem[] = []
          let selected = 0
          let commit: ((item: RichTextSlashItem) => void) | null = null

          function sync(): void {
            renderer?.updateProps({ items, selectedIndex: selected })
            const active = items[selected]
            if (!active) {
              editor.view.dom.removeAttribute('aria-activedescendant')
              return
            }
            const id = optionId(active)
            editor.view.dom.setAttribute('aria-activedescendant', id)
            // The menu is height-capped and scrolls, so without this the selection walks out of
            // sight a few rows down -- on the editor's only keyboard-driven surface. jsdom
            // implements no scrollIntoView at all, hence the optional call.
            document.getElementById(id)?.scrollIntoView?.({ block: 'nearest' })
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
                  onHover: (i: number) => { selected = i; sync() },
                },
              })
              const el = renderer.element as HTMLElement | null
              if (el) {
                el.id = menuId
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
              if (event.key === 'ArrowDown') { selected = (selected + 1) % items.length; sync(); return true }
              if (event.key === 'ArrowUp') { selected = (selected - 1 + items.length) % items.length; sync(); return true }
              if (event.key === 'Enter') { pick(selected); return true }
              // Tab among them: letting it through is what keeps the field's place in the form's
              // tab order.
              return false
            },

            onExit: () => {
              editor.view.dom.removeAttribute('aria-activedescendant')
              editor.view.dom.removeAttribute('aria-owns')
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
