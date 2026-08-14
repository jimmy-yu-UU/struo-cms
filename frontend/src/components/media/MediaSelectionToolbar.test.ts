import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import MediaSelectionToolbar from './MediaSelectionToolbar.vue'
import type { MovePayload } from '../../lib/mediaMove'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { media: {
    selectionCount: '{n} selected', selectionClear: 'Clear selection', moveTo: 'Move to…',
  } } },
})

function mountToolbar(props: { selection: MovePayload; canMoveFiles: boolean; canMoveFolders: boolean }) {
  return mount(MediaSelectionToolbar, { props, global: { plugins: [i18n] } })
}

describe('MediaSelectionToolbar', () => {
  it('renders nothing when the selection is empty', () => {
    const w = mountToolbar({ selection: { files: [], folders: [] }, canMoveFiles: true, canMoveFolders: true })
    expect(w.find('.media-selection-toolbar').exists()).toBe(false)
  })

  it('shows the selected count once the selection is non-empty', () => {
    const w = mountToolbar({ selection: { files: ['f1', 'f2'], folders: ['d1'] }, canMoveFiles: true, canMoveFolders: true })
    expect(w.text()).toContain('3 selected')
  })

  it('emits clear when the clear button is clicked', async () => {
    const w = mountToolbar({ selection: { files: ['f1'], folders: [] }, canMoveFiles: true, canMoveFolders: true })
    await w.find('[data-test="selection-clear"]').trigger('click')
    expect(w.emitted('clear')).toHaveLength(1)
  })

  it('emits requestMove with the whole selection when Move to… is clicked and both grants are present', async () => {
    const selection: MovePayload = { files: ['f1'], folders: ['d1'] }
    const w = mountToolbar({ selection, canMoveFiles: true, canMoveFolders: true })
    await w.find('[data-test="selection-move"]').trigger('click')
    expect(w.emitted('requestMove')?.[0]).toEqual([selection])
  })

  // Permissions: a selection containing files needs canWrite('file'); one containing folders
  // needs canWrite('mediafolder'). Disabling the button alone would already stop VTU's
  // trigger('click') on a native <button>, so also prove the handler itself refuses -- the real
  // guard -- by calling it directly, the same way MediaMoveDialog's own `choose()` test does.
  it('disables Move to… when the selection has files but the user lacks file-write', () => {
    const w = mountToolbar({ selection: { files: ['f1'], folders: [] }, canMoveFiles: false, canMoveFolders: true })
    expect(w.find('[data-test="selection-move"]').attributes('disabled')).toBeDefined()
  })

  it('disables Move to… when the selection has folders but the user lacks mediafolder-write', () => {
    const w = mountToolbar({ selection: { files: [], folders: ['d1'] }, canMoveFiles: true, canMoveFolders: false })
    expect(w.find('[data-test="selection-move"]').attributes('disabled')).toBeDefined()
  })

  it('refuses to emit requestMove via its own handler when the grant is missing (not just the disabled attribute)', () => {
    const selection: MovePayload = { files: ['f1'], folders: [] }
    const w = mountToolbar({ selection, canMoveFiles: false, canMoveFolders: true })
    ;(w.vm as unknown as { onMove: () => void }).onMove()
    expect(w.emitted('requestMove')).toBeUndefined()
  })

  it('enables Move to… when every kind present in the selection is grantable', () => {
    const w = mountToolbar({ selection: { files: ['f1'], folders: ['d1'] }, canMoveFiles: true, canMoveFolders: true })
    expect(w.find('[data-test="selection-move"]').attributes('disabled')).toBeUndefined()
  })
})
