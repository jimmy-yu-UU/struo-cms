// The chapter list is derived from the files, never written down twice. A new
// chapter appears in navigation by existing, and a sidebar label cannot
// disagree with the chapter it points at because the label *is* that chapter's
// own H1. The one hand-written list left is which chapters are pre-rewrite
// ("legacy") — docs/scripts/lib/legacy-chapters.mjs, shared with the
// table-width guard — which this file groups into one collapsed trailing
// section per locale so they stop being interleaved with rewritten chapters
// under the same NN- prefix. That grouping, and the list itself, go away
// together in batch 5. Runs at config load, in Node — VitePress config is not
// browser code.
import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { DefaultTheme } from 'vitepress'
import { LEGACY_CHAPTERS } from '../scripts/lib/legacy-chapters.mjs'
import { groupChapters } from '../scripts/lib/sidebar-groups.mjs'

const GUIDE_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', 'guide')

// Chapters are zero-padded, so a plain lexical sort is already chapter order.
const CHAPTER_FILE = /^\d{2}-.+\.md$/

const LEGACY_GROUP_LABEL: Record<string, string> = {
  en: 'Legacy chapters (being rewritten)',
  'zh-TW': '舊版章節（重寫中）',
}

export function chapterSidebar(locale: string): DefaultTheme.SidebarItem[] {
  const directory = join(GUIDE_ROOT, locale)
  const chapters = readdirSync(directory)
    .filter((name) => CHAPTER_FILE.test(name))
    .sort()

  if (chapters.length === 0) {
    throw new Error(`No chapter files found in ${directory}`)
  }

  const entries = chapters.map((name) => ({
    file: name,
    text: firstHeading(join(directory, name)),
    link: `/${locale}/${name.replace(/\.md$/, '')}`,
  }))

  const legacyLabel = LEGACY_GROUP_LABEL[locale] ?? LEGACY_GROUP_LABEL.en
  return groupChapters(entries, LEGACY_CHAPTERS, legacyLabel)
}

// Positional, not a search: the title is the first line of real content, not
// whatever line happens to match "#" first. A fenced code block containing
// something like "# 204 No Content" must never be mistaken for the heading.
function firstHeading(file: string): string {
  const lines = readFileSync(file, 'utf8').split(/\r?\n/)
  let index = 0

  if (lines[index]?.trim() === '---') {
    const closing = lines.findIndex((line, i) => i > index && line.trim() === '---')
    if (closing === -1) {
      throw new Error(`Unterminated YAML frontmatter in ${file}`)
    }
    index = closing + 1
  }

  while (lines[index] !== undefined && lines[index].trim() === '') {
    index += 1
  }

  // No regex on purpose. Any "#, then whitespace, then the rest" pattern puts
  // two quantifiers that can both match a space next to each other, and that
  // ambiguity is super-linear on a long line of spaces (Sonar S8786). Prefix
  // tests say the same thing with nothing to backtrack over.
  const line = lines[index] ?? ''
  const isHeading = line.startsWith('# ') || line.startsWith('#\t')
  const title = isHeading ? line.slice(1).trim() : ''
  if (!title) {
    throw new Error(
      `Expected an H1 as the first line of content in ${file}, found: ${JSON.stringify(line)}`,
    )
  }
  return title
}
