// The chapter list is derived from the files, never written down twice. A new
// chapter appears in navigation by existing, and a sidebar label cannot
// disagree with the chapter it points at because the label *is* that chapter's
// own H1. Runs at config load, in Node — VitePress config is not browser code.
import { readdirSync, readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const GUIDE_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', 'guide')

// Chapters are zero-padded, so a plain lexical sort is already chapter order.
const CHAPTER_FILE = /^\d{2}-.+\.md$/

export function chapterSidebar(locale: string): { text: string; link: string }[] {
  const directory = join(GUIDE_ROOT, locale)
  const chapters = readdirSync(directory)
    .filter((name) => CHAPTER_FILE.test(name))
    .sort()

  if (chapters.length === 0) {
    throw new Error(`No chapter files found in ${directory}`)
  }

  return chapters.map((name) => ({
    text: firstHeading(join(directory, name)),
    link: `/${locale}/${name.replace(/\.md$/, '')}`,
  }))
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
