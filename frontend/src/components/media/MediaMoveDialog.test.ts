import { describe, it, expect, vi } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaMoveDialog from './MediaMoveDialog.vue'
import type { FolderRow } from '../../lib/folderTree'
import type { MovePayload } from '../../lib/mediaMove'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en: { media: {
    moveTo: 'Move to…',
    moveDescription: 'Choose a destination folder to move the selected item(s) into.',
    moveRoot: 'Root', moveSubmit: 'Move',
  } } },
})

// a is at root, b is a's child, c is b's child (so b's grandchild-of-root), d is a second root
// folder unrelated to the a/b/c chain.
const folders: FolderRow[] = [
  { id: 'a', name: 'A', parentId: null },
  { id: 'b', name: 'B', parentId: 'a' },
  { id: 'c', name: 'C', parentId: 'b' },
  { id: 'd', name: 'D', parentId: null },
]

function mountDialog(payload: MovePayload) {
  return mount(MediaMoveDialog, {
    props: { visible: true, folders, payload },
    global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
  })
}

function indentOf(w: ReturnType<typeof mountDialog>, folderId: string): number {
  const el = w.get(`[data-test="move-option"][data-folder-id="${folderId}"]`)
  const style = (el.element as HTMLElement).style
  return parseFloat(style.paddingLeft || '0')
}

describe('MediaMoveDialog', () => {
  it('lists the root plus every folder, indented by depth', () => {
    const w = mountDialog({ files: [], folders: [] })
    const options = w.findAll('[data-test="move-option"]')
    expect(options).toHaveLength(folders.length + 1)
    expect(options[0].attributes('data-folder-id')).toBe('__root__')
    expect(options[0].text()).toContain('Root')

    const indentA = indentOf(w, 'a')
    const indentB = indentOf(w, 'b')
    const indentC = indentOf(w, 'c')
    expect(indentB).toBeGreaterThan(indentA)
    expect(indentC).toBeGreaterThan(indentB)
  })

  it('disables a folder that would form a cycle with the dragged folder', () => {
    const w = mountDialog({ files: [], folders: ['a'] })
    const disabledIds = ['a', 'b', 'c']
    const enabledIds = ['d']
    for (const id of disabledIds)
      expect(w.get(`[data-test="move-option"][data-folder-id="${id}"]`).attributes('disabled')).toBeDefined()
    for (const id of enabledIds)
      expect(w.get(`[data-test="move-option"][data-folder-id="${id}"]`).attributes('disabled')).toBeUndefined()
    expect(w.get('[data-test="move-option"][data-folder-id="__root__"]').attributes('disabled')).toBeUndefined()
  })

  // With multi-folder batch selections, the docblock promises an option is
  // disabled when moving ANY of the dragged folders there would create a cycle, not just the
  // first one. 'a' and 'd' are both selected; 'a' -> 'b' is a cycle even though 'd' alone would
  // not make 'b' cyclic, so 'b' must still come out disabled.
  it('disables a folder that would form a cycle with any of several dragged folders', () => {
    const w = mountDialog({ files: [], folders: ['a', 'd'] })
    expect(w.get('[data-test="move-option"][data-folder-id="b"]').attributes('disabled')).toBeDefined()
  })

  it('does not disable anything for a files-only payload', () => {
    const w = mountDialog({ files: ['f1'], folders: [] })
    const options = w.findAll('[data-test="move-option"]')
    for (const opt of options)
      expect(opt.attributes('disabled')).toBeUndefined()
  })

  it('emits submit with the chosen folder id', async () => {
    const w = mountDialog({ files: ['f1'], folders: [] })
    await w.get('[data-test="move-option"][data-folder-id="d"]').trigger('click')
    expect(w.emitted('submit')).toEqual([['d']])
    expect(w.emitted('update:visible')).toEqual([[false]])
  })

  it('emits submit with null when the root is chosen', async () => {
    const w = mountDialog({ files: ['f1'], folders: [] })
    await w.get('[data-test="move-option"][data-folder-id="__root__"]').trigger('click')
    expect(w.emitted('submit')).toEqual([[null]])
    expect(w.emitted('update:visible')).toEqual([[false]])
  })

  // Catches a `disabled` attribute rendered without a corresponding guard in the click handler.
  // @vue/test-utils' own trigger('click') checks the element's disabled state itself
  // (isDisabled()) and refuses to dispatch when it's set, so the template path can't reach the
  // handler in a test. Calling the exposed choose() directly with a disabled option is a
  // deliberate unit test of the guard.
  it('emits nothing when a disabled option is activated', () => {
    const w = mountDialog({ files: [], folders: ['a'] })
    const disabledOption = { id: 'a', label: 'A', depth: 0, disabled: true }
    ;(w.vm as unknown as { choose: (o: typeof disabledOption) => void }).choose(disabledOption)
    expect(w.emitted('submit')).toBeUndefined()
    expect(w.emitted('update:visible')).toBeUndefined()
  })

  // The view loads folders sorted by name, not in tree order, so rendering raw `folders` order
  // would let a child (here 'c', child of 'b') appear above unrelated root folders while its
  // indentation still claims it descends from something -- indentation only tells the truth
  // when row order also follows the hierarchy. Feed the dialog a deliberately scrambled input
  // order and assert the rendered sequence is depth-first tree order regardless.
  it('orders options by tree structure, not by the input array order', () => {
    const scrambled: FolderRow[] = [
      { id: 'c', name: 'C', parentId: 'b' },
      { id: 'a', name: 'A', parentId: null },
      { id: 'd', name: 'D', parentId: null },
      { id: 'b', name: 'B', parentId: 'a' },
    ]
    const w = mount(MediaMoveDialog, {
      props: { visible: true, folders: scrambled, payload: { files: [], folders: [] } },
      global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
    })
    const ids = w.findAll('[data-test="move-option"]').map((o) => o.attributes('data-folder-id'))
    expect(ids).toEqual(['__root__', 'a', 'b', 'c', 'd'])
  })

  // reka points DialogContent's aria-describedby at a DialogDescription id whether or not one is
  // rendered, and warns on mount when nothing in the document carries that id -- so the warning is
  // not cosmetic: without a description, assistive tech follows a dangling reference.
  //
  // Mounted attached, unlike every other test in this file, and that is load-bearing: reka resolves
  // the id with document.getElementById, which cannot see a detached wrapper. Mounted the usual way
  // this assertion fails whether or not the description exists, so it would prove nothing.
  it('renders a description, so reka does not warn about a dangling aria-describedby', async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const container = document.body.appendChild(document.createElement('div'))
    const w = mount(MediaMoveDialog, {
      props: { visible: true, folders, payload: { files: [], folders: [] } },
      global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true },
      attachTo: container,
    })
    await flushPromises()
    const messages = warn.mock.calls.map((c) => c.map(String).join(' '))
    expect(messages.filter((m) => m.includes('Missing `Description`'))).toEqual([])
    w.unmount()
    container.remove()
  })
})
