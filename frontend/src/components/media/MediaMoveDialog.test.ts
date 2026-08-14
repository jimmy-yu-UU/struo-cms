import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaMoveDialog from './MediaMoveDialog.vue'
import type { FolderRow } from '../../lib/folderTree'
import type { MovePayload } from '../../lib/mediaMove'

const i18n = createI18n({
  legacy: false,
  locale: 'en',
  fallbackLocale: 'en',
  messages: { en: { media: { moveTo: 'Move to…', moveRoot: 'Root', moveSubmit: 'Move' } } },
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

  // Catches a `disabled` attribute rendered without a corresponding guard in the click handler --
  // VTU's trigger('click') dispatches the event directly and does not respect a native disabled
  // button the way a real user click would, so the handler itself must refuse to act.
  it('emits nothing when a disabled option is activated', async () => {
    const w = mountDialog({ files: [], folders: ['a'] })
    await w.get('[data-test="move-option"][data-folder-id="a"]').trigger('click')
    expect(w.emitted('submit')).toBeUndefined()
    expect(w.emitted('update:visible')).toBeUndefined()
  })
})
