import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RichTextSlashMenu from './RichTextSlashMenu.vue'
import type { RichTextSlashItem } from './richTextSlashCommands'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { fields: { richtext: {
    slashMenu: 'Insert block', slashNoResults: 'No matching commands',
  } } } },
})

function item(id: string, label: string): RichTextSlashItem {
  return { id, label, enLabel: label.toLowerCase(), aliases: [id], run: () => {} }
}

const items = [item('heading2', 'Heading 2'), item('table', 'Table')]

function mountMenu(props: Partial<{ items: RichTextSlashItem[]; selectedIndex: number; idPrefix: string }> = {}) {
  return mount(RichTextSlashMenu, {
    props: { items, selectedIndex: 0, idPrefix: 'rt1', ...props },
    global: { plugins: [i18n] },
  })
}

// @vue/runtime-dom's own event invoker skips calling a listener when the dispatched event's
// timestamp doesn't exceed the moment that listener was attached -- both are stamped with
// Date.now(), whose millisecond resolution this component's mount is fast enough to stay inside,
// so a MouseEvent built and dispatched right after mountMenu() in the same test can silently never
// reach the listener under test, indistinguishable here from the listener being missing. Neither a
// microtask (awaiting nextTick()) nor a zero-delay timer reliably moves the clock past that
// boundary -- both were measured still landing in the same millisecond often enough to reproduce
// this. A few real milliseconds of timer delay did not, across a much larger repeated run.
function afterRealTick(): Promise<void> {
  return new Promise((resolve) => { setTimeout(resolve, 4) })
}

describe('RichTextSlashMenu', () => {
  it('renders one option per item, labelled', () => {
    const w = mountMenu()
    const opts = w.findAll('[role="option"]')
    expect(opts).toHaveLength(2)
    expect(opts[0].text()).toBe('Heading 2')
    expect(opts[1].text()).toBe('Table')
  })

  it('marks only the selected option', () => {
    const w = mountMenu({ selectedIndex: 1 })
    const opts = w.findAll('[role="option"]')
    expect(opts[0].attributes('aria-selected')).toBe('false')
    expect(opts[1].attributes('aria-selected')).toBe('true')
    // aria-selected is the a11y-tree signal; data-active is the sighted-user equivalent that the
    // styling hooks into. Both must move together or a keyboard user sees no visible selection.
    expect(opts[0].attributes('data-active')).toBe('false')
    expect(opts[1].attributes('data-active')).toBe('true')
  })

  // richTextSlashExtension.ts sets aria-activedescendant on the editor to this exact id; the two must agree.
  it('gives each option the id the extension will point at', () => {
    const w = mountMenu({ idPrefix: 'abc' })
    expect(w.findAll('[role="option"]')[1].attributes('id')).toBe('abc-table')
  })

  // preventDefault on mousedown keeps DOM focus (and the live ProseMirror selection) in the editor
  // instead of letting the browser's default mousedown behaviour blur it, so the range the caller's
  // command later reads is still valid at the time the emit is handled.
  it('selects on mousedown and prevents the default', async () => {
    const w = mountMenu()
    await afterRealTick()
    const ev = new MouseEvent('mousedown', { bubbles: true, cancelable: true })
    w.findAll('[role="option"]')[1].element.dispatchEvent(ev)
    expect(ev.defaultPrevented).toBe(true)
    expect(w.emitted('select')).toEqual([[1]])
  })

  // The rows are not the whole target: a mousedown that lands on the p-1 band around them must not
  // blur the editor either, because the extension closes the menu on that blur.
  it('prevents the default for a mousedown on its own chrome, and selects nothing', async () => {
    const w = mountMenu()
    await afterRealTick()
    const ev = new MouseEvent('mousedown', { bubbles: true, cancelable: true })
    w.element.dispatchEvent(ev)
    expect(ev.defaultPrevented).toBe(true)
    expect(w.emitted('select')).toBeUndefined()
  })

  it('reports hover so the caller can move the selection', async () => {
    const w = mountMenu()
    await w.findAll('[role="option"]')[1].trigger('mouseenter')
    expect(w.emitted('hover')).toEqual([[1]])
    // The component is stateless: hover only emits, it never moves the selection itself. That stays
    // the caller's job (selectedIndex is a prop) -- the hovered option must not become selected on
    // its own just because it was hovered.
    expect(w.findAll('[role="option"]')[1].attributes('aria-selected')).toBe('false')
  })

  it('does not emit select on a plain click', async () => {
    const w = mountMenu()
    await w.findAll('[role="option"]')[0].trigger('click')
    expect(w.emitted('select')).toBeUndefined()
  })

  // VueRenderer reads el.firstElementChild once, at construction. A v-if'd root would make
  // that null forever, and props.mount() would have nothing to mount.
  it('keeps rendering its root element when the list is empty', () => {
    const w = mountMenu({ items: [] })
    expect(w.element.nodeType).toBe(Node.ELEMENT_NODE)
    expect(w.attributes('role')).toBe('listbox')
    expect(w.text()).toContain('No matching commands')
    expect(w.findAll('[role="option"]')).toHaveLength(0)
  })

  it('labels the listbox', () => {
    expect(mountMenu().attributes('aria-label')).toBe('Insert block')
  })
})
