import { describe, it, expect } from 'vitest'
import { LayoutGrid, Images, Settings, Trash2, Pencil, File as FileIcon, Folder, Tag } from '@lucide/vue'
import { resolveIcon } from './icons'

describe('resolveIcon', () => {
  it('resolves a full PrimeIcons class string', () => {
    expect(resolveIcon('pi pi-th-large')).toBe(LayoutGrid)
    expect(resolveIcon('pi pi-images')).toBe(Images)
    expect(resolveIcon('pi pi-cog')).toBe(Settings)
  })

  it('resolves a bare pi- token', () => {
    expect(resolveIcon('pi-trash')).toBe(Trash2)
    expect(resolveIcon('pi-pencil')).toBe(Pencil)
  })

  // Backend [CmsCollection(Icon = "...")] carries a semantic name, not a CSS class.
  // The old SidebarNavItem treated it as a class, so these rendered nothing at all.
  it('resolves the semantic names the backend emits', () => {
    expect(resolveIcon('article')).toBeDefined()
    expect(resolveIcon('folder')).toBe(Folder)
    expect(resolveIcon('tag')).toBe(Tag)
  })

  it('falls back to a generic file icon', () => {
    expect(resolveIcon(null)).toBe(FileIcon)
    expect(resolveIcon(undefined)).toBe(FileIcon)
    expect(resolveIcon('')).toBe(FileIcon)
    expect(resolveIcon('no-such-icon-anywhere')).toBe(FileIcon)
  })

  // Tokens the Step 1 grep found that the brief's skeleton map did not cover. Each is a real
  // `pi pi-*` or `pi-*` class still present in src/ today (see task-5-report.md for the full list
  // and where each one lives).
  it('resolves tokens missing from the brief skeleton but present in src/', () => {
    expect(resolveIcon('pi pi-arrow-up')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-arrow-down')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-chevron-left')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-copy')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-external-link')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-folder-plus')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-list')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-sort-numeric-down')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-table')).not.toBe(FileIcon)
    // The static half of RichTextInput.vue's dynamic `pi-align-${direction}` class literal.
    expect(resolveIcon('pi pi-align-')).not.toBe(FileIcon)
  })
})
