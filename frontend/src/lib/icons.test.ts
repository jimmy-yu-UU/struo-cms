import { describe, it, expect } from 'vitest'
import { LayoutGrid, Images, Settings, Trash2, Pencil, File as FileIcon, Folder, Tag, ChevronRight } from '@lucide/vue'
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

  // Backend [CmsCollection(Icon = "...")] carries a semantic name, not a CSS class; resolveIcon
  // must map these semantic names to an icon component.
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

  // ICON_MAP is a plain object literal; a hostile or careless collection-metadata name must not
  // reach up into Object.prototype and resolve to something other than the documented fallback.
  it('falls back for Object.prototype property names', () => {
    expect(resolveIcon('constructor')).toBe(FileIcon)
    expect(resolveIcon('toString')).toBe(FileIcon)
    expect(resolveIcon('hasOwnProperty')).toBe(FileIcon)
  })

  // Each token below is a real `pi pi-*` or `pi-*` class still present in src/ today.
  it('resolves tokens missing from the brief skeleton but present in src/', () => {
    expect(resolveIcon('pi pi-arrow-up')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-arrow-down')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-chevron-left')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-copy')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-folder-plus')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-list')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-sort-numeric-down')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-table')).not.toBe(FileIcon)
  })

  // RichTextInput.vue builds these classes dynamically; the literal token never appears in
  // source, only the "align-" prefix does (see icons.ts and iconCoverage.test.ts). Mapped ahead
  // of that component's own migration so the real runtime values resolve correctly today.
  it('resolves the four alignment directions RichTextInput.vue builds dynamically', () => {
    expect(resolveIcon('pi pi-align-left')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-align-center')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-align-right')).not.toBe(FileIcon)
    expect(resolveIcon('pi pi-align-justify')).not.toBe(FileIcon)
  })

  // A class string may carry other utility classes alongside the icon token, in either order —
  // MediaLibraryView.vue's `.media-crumb__sep` breadcrumb separator puts the utility class after
  // the icon ("pi pi-angle-right media-crumb__sep"); TheSidebar.vue and MediaFolderCards.vue put
  // it before ("nav-icon pi pi-folder"). A trailing .pop() would silently pick the wrong word in
  // the first case.
  it('picks the pi- token out of a class string regardless of position', () => {
    expect(resolveIcon('nav-icon pi pi-folder')).toBe(Folder)
    expect(resolveIcon('pi pi-angle-right media-crumb__sep')).toBe(ChevronRight)
  })
})
