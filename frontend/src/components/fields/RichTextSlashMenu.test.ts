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
  return { id, label, aliases: [id], run: () => {} }
}

const items = [item('heading2', 'Heading 2'), item('table', 'Table')]

function mountMenu(props: Partial<{ items: RichTextSlashItem[]; selectedIndex: number; idPrefix: string }> = {}) {
  return mount(RichTextSlashMenu, {
    props: { items, selectedIndex: 0, idPrefix: 'rt1', ...props },
    global: { plugins: [i18n] },
  })
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
  })

  // Task 6 sets aria-activedescendant on the editor to this exact id; the two must agree.
  it('gives each option the id the extension will point at', () => {
    const w = mountMenu({ idPrefix: 'abc' })
    expect(w.findAll('[role="option"]')[1].attributes('id')).toBe('abc-table')
  })

  // A click that lets its mousedown through blurs the editor, and the suggestion state dies with
  // the focus -- so the emit would land on a menu that is already gone.
  it('selects on mousedown and prevents the default', async () => {
    const w = mountMenu()
    const ev = new MouseEvent('mousedown', { bubbles: true, cancelable: true })
    w.findAll('[role="option"]')[1].element.dispatchEvent(ev)
    expect(ev.defaultPrevented).toBe(true)
    expect(w.emitted('select')).toEqual([[1]])
  })

  it('reports hover so the caller can move the selection', async () => {
    const w = mountMenu()
    await w.findAll('[role="option"]')[1].trigger('mouseenter')
    expect(w.emitted('hover')).toEqual([[1]])
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
