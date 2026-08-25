import type { Component } from 'vue'
import type { Editor } from '@tiptap/vue-3'
import {
  AlignLeft, AlignCenter, AlignRight, AlignJustify, List, ListOrdered,
  Quote, Code2, Link as LinkIcon, Minus, Image as ImageIcon, Undo2, Redo2,
} from '@lucide/vue'

export type RichTextCommandGroup = 'inline' | 'block' | 'insert' | 'history'

export interface RichTextCommandContext {
  t: (key: string) => string
  openImageDialog: () => void
  // Resolves with the dialog's confirmed value, 'remove' if its Remove button was used, or null on
  // cancel. Validation (isAllowedLinkUrl) lives entirely in the dialog now, not here -- by the time
  // this resolves with a value, the href has already passed it.
  openLinkDialog: (
    initial: { href: string; newTab: boolean; canRemove: boolean }
  ) => Promise<{ href: string; newTab: boolean } | 'remove' | null>
}

export interface RichTextCommand {
  id: string
  // Explicit rather than derived from id: strike/orderedList/hr/image map to
  // strikethrough/numberedList/horizontalRule/insertImage, so deriving the key would either be
  // wrong for these four or need its own exception list -- carrying the key plainly is simpler.
  labelKey: string
  // Not consumed by any production code in this batch. It is the surface RT-5 (inline) and RT-7
  // (block + insert) read from, defined once here so neither batch has to invent its own partition.
  group: RichTextCommandGroup
  icon: Component | null
  glyph: string | null
  // Null for subscript/superscript: they render their glyph as bare text today with no wrapping
  // element, unlike bold/italic/strike, which wrap theirs in a tag.
  glyphTag: 'b' | 'i' | 's' | null
  isActive: ((editor: Editor) => boolean) | null
  run: (editor: Editor, ctx: RichTextCommandContext) => void
}

// Every active-state toolbar button carries data-[active=true]:hover:bg-primary
// (and its dark:-prefixed form) alongside the plain data-[active=true]:bg-primary. The ghost
// variant's own hover:bg-accent hover:text-accent-foreground carries just one modifier
// (hover), so an override written with just one modifier (data-[active=true]) ties it on CSS
// specificity and the winner is whichever rule the stylesheet happens to emit later — not a
// reliable outcome. Stacking data-[active=true] AND hover onto the override selector adds an
// attribute-selector component that the plain hover rule lacks, so it wins on specificity
// regardless of emission order. Dark mode needs a second, dark:-prefixed copy of the
// background rule because the vendored ghost variant carries a dark:hover:bg-accent/50
// override of its own: that selector's :is()-wrapped dark-mode wrapper is itself a
// specificity component, so only a same-shape dark:-prefixed override outweighs it — the
// undecorated data-[active=true]:hover:bg-primary rule would tie it, not beat it. The text
// pairing has no such dark-only competitor (ghost never overrides hover text colour for
// dark), so one undecorated override rule already wins in both colour schemes.
export const RICHTEXT_ACTIVE_BUTTON_CLASS =
  'data-[active=true]:bg-primary data-[active=true]:text-primary-foreground '
  + 'data-[active=true]:hover:bg-primary data-[active=true]:hover:text-primary-foreground '
  + 'dark:data-[active=true]:hover:bg-primary'

function mark(
  id: string, labelKey: string, glyph: string, glyphTag: 'b' | 'i' | 's' | null,
  toggle: (editor: Editor) => void,
): RichTextCommand {
  return {
    id, labelKey, group: 'inline', icon: null, glyph, glyphTag,
    isActive: (editor) => editor.isActive(id),
    run: (editor) => toggle(editor),
  }
}

function align(
  dir: 'left' | 'center' | 'right' | 'justify', icon: Component,
): RichTextCommand {
  const id = `align${dir.charAt(0).toUpperCase()}${dir.slice(1)}`
  return {
    id, labelKey: `fields.richtext.${id}`, group: 'block', icon, glyph: null, glyphTag: null,
    isActive: (editor) => editor.isActive({ textAlign: dir }),
    run: (editor) => { editor.chain().focus().setTextAlign(dir).run() },
  }
}

export const TOOLBAR_BEFORE_HEADINGS: ReadonlyArray<RichTextCommand> = [
  mark('bold', 'fields.richtext.bold', 'B', 'b', (e) => { e.chain().focus().toggleBold().run() }),
  mark('italic', 'fields.richtext.italic', 'I', 'i', (e) => { e.chain().focus().toggleItalic().run() }),
  mark('strike', 'fields.richtext.strikethrough', 'S', 's', (e) => { e.chain().focus().toggleStrike().run() }),
  align('left', AlignLeft), align('center', AlignCenter), align('right', AlignRight), align('justify', AlignJustify),
]

export const TOOLBAR_BEFORE_COLOR: ReadonlyArray<RichTextCommand> = [
  mark('subscript', 'fields.richtext.subscript', 'x₂', null, (e) => { e.chain().focus().toggleSubscript().run() }),
  mark('superscript', 'fields.richtext.superscript', 'x²', null, (e) => { e.chain().focus().toggleSuperscript().run() }),
  {
    id: 'bulletList', labelKey: 'fields.richtext.bulletList', group: 'block', icon: List, glyph: null, glyphTag: null,
    isActive: (editor) => editor.isActive('bulletList'),
    run: (editor) => { editor.chain().focus().toggleBulletList().run() },
  },
  {
    id: 'orderedList', labelKey: 'fields.richtext.numberedList', group: 'block', icon: ListOrdered, glyph: null, glyphTag: null,
    isActive: (editor) => editor.isActive('orderedList'),
    run: (editor) => { editor.chain().focus().toggleOrderedList().run() },
  },
  {
    id: 'blockquote', labelKey: 'fields.richtext.blockquote', group: 'block', icon: Quote, glyph: null, glyphTag: null,
    isActive: (editor) => editor.isActive('blockquote'),
    run: (editor) => { editor.chain().focus().toggleBlockquote().run() },
  },
  {
    id: 'codeBlock', labelKey: 'fields.richtext.codeBlock', group: 'block', icon: Code2, glyph: null, glyphTag: null,
    isActive: (editor) => editor.isActive('codeBlock'),
    run: (editor) => { editor.chain().focus().toggleCodeBlock().run() },
  },
  {
    id: 'link', labelKey: 'fields.richtext.link', group: 'inline', icon: LinkIcon, glyph: null, glyphTag: null,
    isActive: (editor) => editor.isActive('link'),
    // void-returning, like image's run() above: the dialog is not modal, so this cannot await its
    // result without changing run()'s own return type, and RichTextCommand.run is declared void on
    // purpose (RichTextCommandButton's @click has nothing to await either). The chain below runs
    // once the promise resolves, whenever that turns out to be.
    run: (editor, ctx) => {
      const attrs = editor.getAttributes('link')
      const canRemove = editor.isActive('link')
      void ctx.openLinkDialog({
        href: (attrs.href as string | undefined) ?? '',
        newTab: attrs.target === '_blank',
        canRemove,
      }).then((result) => {
        if (result === null) return // cancelled
        if (result === 'remove') {
          editor.chain().focus().extendMarkRange('link').unsetLink().run()
          return
        }
        // extendMarkRange('link') re-targets the whole existing link when the call started from a
        // caret inside one, rather than a zero-length fragment at that caret.
        // rel is never passed here -- the backend derives and owns it (Global Constraints); a mark
        // created via setLink with no rel falls back to the Link extension's own attribute default,
        // which RichTextInput.vue's HTMLAttributes: { rel: null } makes exactly `null`.
        editor.chain().focus().extendMarkRange('link')
          .setLink({ href: result.href, target: result.newTab ? '_blank' : null }).run()
      })
    },
  },
  {
    id: 'hr', labelKey: 'fields.richtext.horizontalRule', group: 'insert', icon: Minus, glyph: null, glyphTag: null,
    isActive: null,
    run: (editor) => { editor.chain().focus().setHorizontalRule().run() },
  },
  {
    id: 'image', labelKey: 'fields.richtext.insertImage', group: 'insert', icon: ImageIcon, glyph: null, glyphTag: null,
    isActive: null,
    run: (_editor, ctx) => ctx.openImageDialog(),
  },
]

export const TOOLBAR_AFTER_TABLE: ReadonlyArray<RichTextCommand> = [
  {
    id: 'undo', labelKey: 'fields.richtext.undo', group: 'history', icon: Undo2, glyph: null, glyphTag: null,
    isActive: null,
    run: (editor) => { editor.chain().focus().undo().run() },
  },
  {
    id: 'redo', labelKey: 'fields.richtext.redo', group: 'history', icon: Redo2, glyph: null, glyphTag: null,
    isActive: null,
    run: (editor) => { editor.chain().focus().redo().run() },
  },
]

export const RICH_TEXT_COMMANDS: ReadonlyArray<RichTextCommand> = [
  ...TOOLBAR_BEFORE_HEADINGS, ...TOOLBAR_BEFORE_COLOR, ...TOOLBAR_AFTER_TABLE,
]
