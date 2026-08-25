import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { markRaw } from 'vue'
import { createI18n } from 'vue-i18n'
import { List } from '@lucide/vue'
import type { Editor } from '@tiptap/vue-3'
import RichTextCommandButton from './RichTextCommandButton.vue'
import { RICHTEXT_ACTIVE_BUTTON_CLASS, type RichTextCommand } from './richTextCommands'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: {
    bulletList: 'Bullet list', bold: 'Bold', subscript: 'Subscript',
  } } } },
})
// Named globalOpts, not `global`: a module-scope `const global` shadows Node's own binding, which
// is legal but reads as a mistake. RichTextInput.test.ts uses the same name.
const globalOpts = { plugins: [i18n] }

function cmd(over: Partial<RichTextCommand> = {}): RichTextCommand {
  return {
    id: 'bulletList', labelKey: 'fields.richtext.bulletList', group: 'block',
    icon: List, glyph: null, glyphTag: null,
    isActive: () => false, run: () => {},
    ...over,
  }
}

// The component only ever hands the editor to command.isActive; it calls nothing on it itself, so
// every command here answers without looking at its argument and a bare sentinel is enough. The one
// test that cares whether the editor arrives at all asserts identity against this exact object.
//
// markRaw, and not for tidiness: VTU mounts props through a reactive parent, so a plain object
// arrives at the child as a reactive proxy and fails an identity assertion. tiptap/vue-3's own
// Editor constructor ends with `return markRaw(this)`, so the real editor is never proxied either
// -- marking the sentinel the same way makes this stub behave like production rather than papering
// over the difference with toRaw at the assertion.
const editor = markRaw({ __sentinel: 'editor' }) as unknown as Editor

describe('RichTextCommandButton', () => {
  it('carries the command id, an explicit button type, and both accessible names', () => {
    const w = mount(RichTextCommandButton, { props: { command: cmd(), editor }, global: globalOpts })
    const btn = w.get('[data-cmd="bulletList"]')
    // ItemForm.vue wraps every field in <form @submit.prevent>, so a bare <button> here would
    // submit the whole record on its first click.
    expect(btn.attributes('type')).toBe('button')
    expect(btn.attributes('aria-label')).toBe('Bullet list')
    expect(btn.attributes('title')).toBe('Bullet list')
    // This file is the repo's only unstubbed Button mount, so it is the only place that can pin
    // these -- dropping variant="ghost" would silently swap in the default variant's bg-primary.
    expect(btn.attributes('data-variant')).toBe('ghost')
    expect(btn.attributes('data-size')).toBe('icon')
  })

  it('renders data-active as the literal string, and omits it entirely when there is no active state', () => {
    expect(mount(RichTextCommandButton, { props: { command: cmd({ isActive: () => false }), editor }, global: globalOpts })
      .get('[data-cmd]').attributes('data-active')).toBe('false')
    expect(mount(RichTextCommandButton, { props: { command: cmd({ isActive: () => true }), editor }, global: globalOpts })
      .get('[data-cmd]').attributes('data-active')).toBe('true')
    // hr/image/undo/redo have no active state; their buttons carry no attribute at all today.
    expect(mount(RichTextCommandButton, { props: { command: cmd({ isActive: null }), editor }, global: globalOpts })
      .get('[data-cmd]').attributes('data-active')).toBeUndefined()
  })

  // The reason this component reads the active state instead of taking it as a prop is that it
  // hands the editor to the command itself. A component that called isActive() with no argument
  // would still render data-active correctly for every stub command in this file, since none of
  // them look at what they are passed -- so the argument's identity is what has to be asserted,
  // not the rendered result.
  it('calls isActive with the editor it was given', () => {
    const seen: unknown[] = []
    mount(RichTextCommandButton, {
      props: { command: cmd({ isActive: (e) => { seen.push(e); return false } }), editor },
      global: globalOpts,
    })
    expect(seen).toContain(editor)
  })

  // The override only ever paints through data-active, so a command that cannot be active must not
  // carry it -- that is what those four buttons look like today. The active-capable case (below)
  // checks the whole class list against the FULL override string (not just its first token), since
  // a wrapper that dropped only part of the override (e.g. the dark: clause) would still pass a
  // "first token only" check. The stateless case checks for the absence of the data-[active=true]
  // modifier itself, which is the maximally strict negative.
  it('applies the active-state override class only to commands that can be active', () => {
    expect(mount(RichTextCommandButton, { props: { command: cmd(), editor }, global: globalOpts })
      .get('[data-cmd]').attributes('class')).toContain(RICHTEXT_ACTIVE_BUTTON_CLASS)
    expect(mount(RichTextCommandButton, { props: { command: cmd({ isActive: null }), editor }, global: globalOpts })
      .get('[data-cmd]').attributes('class') ?? '').not.toContain('data-[active=true]')
  })

  it('renders a lucide icon when the command has one', () => {
    const w = mount(RichTextCommandButton, { props: { command: cmd(), editor }, global: globalOpts })
    expect(w.find('.lucide-list').exists()).toBe(true)
  })

  // bold/italic/strike wrap their letter in a semantic tag; subscript/superscript show bare text.
  // A wrapper element around the glyph would still let `.text()` return "x₂" (jsdom's textContent
  // flattens descendants), so the tag/element assertions below are what actually catch a stray
  // wrapper -- the text equality alone would not.
  it('wraps a glyph in its tag when given one, and leaves it bare when not', () => {
    const tagged = mount(RichTextCommandButton, {
      props: { command: cmd({ id: 'bold', labelKey: 'fields.richtext.bold', icon: null, glyph: 'B', glyphTag: 'b' }), editor },
      global: globalOpts,
    })
    const boldBtn = tagged.get('[data-cmd="bold"]')
    expect(boldBtn.get('b').text()).toBe('B')
    // A tagged command must have its glyph wrapped in exactly that tag and nothing else -- if
    // 'bold' rendered an unwrapped glyph, boldBtn.get('b') above would already have thrown, but
    // this also rules out an extra, unrelated wrapper (e.g. a <span>) sitting around the <b>.
    expect(boldBtn.element.children.length).toBe(1)
    expect(boldBtn.element.children[0]?.tagName).toBe('B')

    const bare = mount(RichTextCommandButton, {
      props: { command: cmd({ id: 'subscript', labelKey: 'fields.richtext.subscript', icon: null, glyph: 'x₂', glyphTag: null }), editor },
      global: globalOpts,
    })
    const subBtn = bare.get('[data-cmd="subscript"]')
    expect(subBtn.text()).toBe('x₂')
    // No element children at all -- not just "no <span>" -- is what "bare text" actually means.
    expect(subBtn.element.children.length).toBe(0)
  })

  it('forwards disabled to the rendered button, and omits the attribute when not disabled', () => {
    const disabled = mount(RichTextCommandButton, { props: { command: cmd(), disabled: true, editor }, global: globalOpts })
    expect(disabled.get('[data-cmd]').attributes('disabled')).toBeDefined()
    const enabled = mount(RichTextCommandButton, { props: { command: cmd(), editor }, global: globalOpts })
    expect(enabled.get('[data-cmd]').attributes('disabled')).toBeUndefined()
  })

  // Split from the disabled check above rather than combined: VTU's own
  // trigger() deliberately no-ops a 'click' on any element it considers disabled (BaseWrapper.trigger
  // checks isDisabled() before dispatching at all) -- a real, unstubbed <button disabled> would make
  // this assertion deterministically fail, so disabled and the emit have to be exercised on
  // separate mounts.
  it('emits run on click', async () => {
    const w = mount(RichTextCommandButton, { props: { command: cmd(), editor }, global: globalOpts })
    await w.get('[data-cmd]').trigger('click')
    expect(w.emitted('run')).toHaveLength(1)
  })
})
