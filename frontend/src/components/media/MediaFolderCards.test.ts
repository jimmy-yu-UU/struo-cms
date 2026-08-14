import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaFolderCards from './MediaFolderCards.vue'
import type { FolderRow } from '../../lib/folderTree'
import { DRAG_MIME, serializeMovePayload } from '../../lib/mediaMove'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en: { media: { folderRename: 'Rename folder', folderDelete: 'Delete folder' } } },
})

const folders: FolderRow[] = [
  { id: 'a', name: 'Alpha', parentId: null },
  { id: 'b', name: 'Beta', parentId: null },
]

function mountCards(canManage: boolean) {
  return mount(MediaFolderCards, {
    props: { folders, canManage },
    global: { plugins: [i18n] },
  })
}

describe('MediaFolderCards', () => {
  it('renders a card per folder with its name', () => {
    const w = mountCards(false)
    const cards = w.findAll('.folder-card')
    expect(cards).toHaveLength(2)
    expect(cards[0].text()).toContain('Alpha')
    expect(cards[1].text()).toContain('Beta')
  })

  it('emits open with the folder id when a card is clicked', async () => {
    const w = mountCards(false)
    await w.findAll('.folder-card')[1].trigger('click')
    expect(w.emitted('open')).toEqual([['b']])
  })

  it('shows no manage buttons when canManage is false', () => {
    const w = mountCards(false)
    expect(w.findAllComponents({ name: 'Button' })).toHaveLength(0)
  })

  it('shows rename/delete buttons when canManage is true and stops click propagation', async () => {
    const w = mountCards(true)
    const firstCard = w.findAll('.folder-card')[0]
    const buttons = firstCard.findAllComponents({ name: 'Button' })
    expect(buttons).toHaveLength(2)

    await buttons[0].trigger('click')
    expect(w.emitted('rename')).toEqual([[folders[0]]])
    expect(w.emitted('open')).toBeUndefined()

    await buttons[1].trigger('click')
    expect(w.emitted('remove')).toEqual([[folders[0]]])
    expect(w.emitted('open')).toBeUndefined()
  })

  it('opens the folder when Enter is pressed on the card itself', async () => {
    const w = mountCards(false)
    await w.findAll('.folder-card')[1].trigger('keydown.enter')
    expect(w.emitted('open')).toEqual([['b']])
  })

  // Regression guard for the a11y finding: keydown bubbles from the inner rename/delete buttons
  // up to the card (unlike click, whose handlers use .stop), so plain @keydown.enter on the card
  // used to race folder navigation against the rename dialog / delete confirm opening. `.self`
  // restricts the card's handler to events whose target IS the card, not a descendant.
  it('does NOT emit open when Enter is pressed on an inner rename/delete button', async () => {
    const w = mountCards(true)
    const firstCard = w.findAll('.folder-card')[0]
    const buttons = firstCard.findAllComponents({ name: 'Button' })

    await buttons[0].trigger('keydown.enter')
    expect(w.emitted('open')).toBeUndefined()

    await buttons[1].trigger('keydown.enter')
    expect(w.emitted('open')).toBeUndefined()
  })

  // findAllComponents({ name: 'Button' }) above matches PrimeVue's Button and the vendored
  // ui/button by name equally, so it cannot tell which is mounted. These assertions target what
  // the shadcn-vue migration actually changed: lucide icon identity (PrimeVue rendered `<i
  // class="pi pi-folder">` etc., not an SVG) and an explicit type="button" on both action buttons.
  it('renders the folder icon as a lucide icon, not a PrimeVue pi icon', () => {
    const w = mountCards(false)
    expect(w.find('.lucide-folder').exists()).toBe(true)
    expect(w.find('.pi').exists()).toBe(false)
  })

  it('renders the rename/delete actions as lucide icons with an explicit type="button"', () => {
    const w = mountCards(true)
    const firstCard = w.findAll('.folder-card')[0]
    expect(firstCard.find('.lucide-pencil').exists()).toBe(true)
    expect(firstCard.find('.lucide-trash-2').exists()).toBe(true)
    expect(firstCard.find('.pi').exists()).toBe(false)

    const buttons = firstCard.findAllComponents({ name: 'Button' })
    expect(buttons[0].attributes('type')).toBe('button')
    expect(buttons[1].attributes('type')).toBe('button')
  })

  it('emits dropOn with the parsed payload when a media drag is dropped on a card', async () => {
    const w = mount(MediaFolderCards, { props: { folders, canManage: true }, global: { plugins: [i18n] } })
    const payload = { files: ['f1'], folders: [] }
    const dataTransfer = {
      types: [DRAG_MIME],
      getData: (t: string) => (t === DRAG_MIME ? serializeMovePayload(payload) : ''),
      dropEffect: '',
    }
    await w.find('.folder-card').trigger('drop', { dataTransfer })
    expect(w.emitted('dropOn')?.[0]).toEqual([folders[0].id, payload])
  })

  it('ignores a drop carrying no media payload', async () => {
    const w = mount(MediaFolderCards, { props: { folders, canManage: true }, global: { plugins: [i18n] } })
    const dataTransfer = { types: ['text/plain'], getData: () => 'hello', dropEffect: '' }
    await w.find('.folder-card').trigger('drop', { dataTransfer })
    expect(w.emitted('dropOn')).toBeUndefined()
  })

  it('clears the drop-highlight on dragleave', async () => {
    const w = mount(MediaFolderCards, { props: { folders, canManage: true }, global: { plugins: [i18n] } })
    const card = w.find('.folder-card')
    const dataTransfer = { types: [DRAG_MIME], getData: () => '', dropEffect: '' }
    await card.trigger('dragover', { dataTransfer })
    expect(card.attributes('data-dropping')).toBe('true')
    await card.trigger('dragleave')
    expect(card.attributes('data-dropping')).toBeUndefined()
  })

  // Permissions: folder moves require canWrite('mediafolder') -- without that grant the card
  // must not be a drag source at all, regardless of canManage (which also covers delete-only
  // users who should still be able to see rename/delete but not drag folders around).
  it('is not draggable when canMove is false or unset', () => {
    const w = mount(MediaFolderCards, { props: { folders, canManage: true }, global: { plugins: [i18n] } })
    expect(w.find('.folder-card').attributes('draggable')).toBeUndefined()
  })

  it('is draggable when canMove is true', () => {
    const w = mount(MediaFolderCards, { props: { folders, canManage: true, canMove: true }, global: { plugins: [i18n] } })
    expect(w.find('.folder-card').attributes('draggable')).toBe('true')
  })
})
