import type { Component } from 'vue'
import {
  LayoutGrid, Images, Settings, Folder, FolderPlus, File as FileIcon, FileText, FileType,
  FileSpreadsheet, Tag, Trash2, Pencil, Eye, Plus, Search, Undo2, Copy, ExternalLink,
  ChevronDown, ChevronLeft, ChevronRight, ArrowUp, ArrowDown, AlignLeft, Menu, User, LogOut,
  Sun, Moon, Check, X, Upload, History, RotateCcw, List, ListOrdered, Table,
  Megaphone, Newspaper, Image as ImageIcon, Video, Volume2,
} from '@lucide/vue'

// One table, two input dialects:
//   * PrimeIcons tokens ("pi pi-th-large" / "pi-th-large") — what this codebase's own
//     templates say today.
//   * Semantic names ("article", "folder") — what the backend's [CmsCollection(Icon = "...")]
//     emits and what docs/guide/*/04-defining-a-collection.md documents.
// Keys are stored WITHOUT the "pi-" prefix so both dialects normalise to the same lookup.
//
// Every key here is required by one of two things: a literal `pi-*` token still present
// somewhere under src/ (enforced by frontend/tests/iconCoverage.test.ts), or a semantic name a
// [CmsCollection(Icon = "...")] attribute actually emits (article/folder/tag from
// samples/Struo.Sample.Blog, megaphone from docs/guide/*/04-defining-a-collection.md). There are
// no speculative entries — see task-5-report.md for the tokens removed from the brief's skeleton
// because nothing in the repo uses them.
export const ICON_MAP: Record<string, Component> = {
  // --- navigation / shell ---
  'th-large': LayoutGrid,
  images: Images,
  cog: Settings,
  bars: Menu,
  user: User,
  'sign-out': LogOut,
  sun: Sun,
  moon: Moon,
  'angle-down': ChevronDown,
  'angle-left': ChevronLeft,
  'angle-right': ChevronRight,
  'chevron-left': ChevronLeft,

  // --- actions ---
  plus: Plus,
  search: Search,
  pencil: Pencil,
  eye: Eye,
  trash: Trash2,
  undo: Undo2,
  check: Check,
  times: X,
  upload: Upload,
  copy: Copy,
  'external-link': ExternalLink,
  history: History,
  replay: RotateCcw,

  // --- reordering / alignment indicators ---
  'arrow-up': ArrowUp,
  'arrow-down': ArrowDown,
  // Static half of RichTextInput.vue's dynamic `pi-align-${direction}` class literal — the
  // coverage scanner only sees source text, not the interpolated runtime value.
  'align-': AlignLeft,

  // --- rich text: lists / tables ---
  list: List,
  'sort-numeric-down': ListOrdered,
  table: Table,

  // --- file / media kinds ---
  file: FileIcon,
  'file-edit': FileText,
  'file-pdf': FileType,
  'file-word': FileText,
  'file-excel': FileSpreadsheet,
  image: ImageIcon,
  video: Video,
  'volume-up': Volume2,
  folder: Folder,
  'folder-plus': FolderPlus,

  // --- semantic names emitted by [CmsCollection(Icon = "...")] ---
  article: Newspaper,
  tag: Tag,
  megaphone: Megaphone,
}

/**
 * Resolves an icon name to a lucide component.
 *
 * Accepts "pi pi-foo", "pi-foo" or a bare semantic name. Unknown names fall back to a
 * generic file icon rather than throwing — collection metadata is author-supplied and a
 * typo must not break the sidebar.
 */
export function resolveIcon(name: string | null | undefined): Component {
  if (!name) return FileIcon
  const token = name.trim().split(/\s+/).pop() ?? ''
  const key = token.startsWith('pi-') ? token.slice(3) : token
  return ICON_MAP[key] ?? FileIcon
}
