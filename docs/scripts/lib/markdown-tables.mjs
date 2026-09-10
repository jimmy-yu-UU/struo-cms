// The manual renders in VitePress's default content column (about 688px).
// A table wider than that scrolls horizontally, which the maintainer ruled
// unacceptable on 2026-09-10: a wide cell is as unreadable as a long paragraph.
// Nothing in `vitepress build` measures this, so the guard lives here.
//
// The check is deliberately mechanical and source-side. It measures the text a
// reader will see — inline code, link targets and emphasis markers stripped —
// with CJK characters counted double, because at the manual's font size one
// CJK glyph takes about the space of two Latin ones. There is no per-table
// exemption on purpose: a table that cannot fit must be rewritten as a list or
// a section, not waved through.

// Spec thresholds (docs/_archive-local/superpowers/specs/2026-09-10-docs-rewrite-design.md §5).
// One definition, shared by the CLI and its tests.
export const LIMITS = Object.freeze({ maxColumns: 4, maxCellWidth: 60 })

// East Asian Wide/Fullwidth ranges that a reader perceives as two columns:
// Hangul Jamo, CJK radicals through Yi, Hangul syllables, CJK compatibility
// ideographs, CJK compatibility forms, fullwidth ASCII/punctuation, fullwidth
// currency signs, and the supplementary ideographic planes.
const CJK =
  /[\u1100-\u115F\u2E80-\uA4CF\uAC00-\uD7A3\uF900-\uFAFF\uFE30-\uFE4F\uFF00-\uFF60\uFFE0-\uFFE6]|[\u{20000}-\u{3FFFD}]/u

export function displayWidth(text) {
  let width = 0
  for (const character of text) {
    width += CJK.test(character) ? 2 : 1
  }
  return width
}

// Strip what the reader does not see. Order matters: links first so a code
// span inside link text is still unwrapped by the backtick pass afterwards.
export function cellText(rawCell) {
  return rawCell
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .replace(/`([^`]*)`/g, '$1')
    .replace(/\*\*|__/g, '')
    .trim()
}

// Same fence rule as check-rendered-chapters.mjs: 3+ backticks or tildes,
// indented up to 3 spaces; the closer reuses the opener's character and is at
// least as long. Tables in code samples must never be measured — that false
// positive is exactly what would get this guard deleted later.
const FENCE_LINE = /^ {0,3}(`{3,}|~{3,})/
const TABLE_ROW = /^ {0,3}\|.*\|\s*$/
const DELIMITER_ROW = /^\s*\|(\s*:?-+:?\s*\|)+\s*$/

// Split on pipes that are not escaped. The leading and trailing pipe are
// dropped first so an empty edge cell is not invented.
function splitCells(row) {
  return row.trim().slice(1, -1).split(/(?<!\\)\|/)
}

export function checkTables(markdown, limits) {
  const lines = markdown.split(/\r?\n/)
  const violations = []
  let fence = null

  lines.forEach((line, index) => {
    const opener = FENCE_LINE.exec(line)
    if (fence) {
      if (opener && opener[1].startsWith(fence.char) && opener[1].length >= fence.length) {
        fence = null
      }
      return
    }
    if (opener) {
      fence = { char: opener[1][0], length: opener[1].length }
      return
    }
    if (!TABLE_ROW.test(line) || DELIMITER_ROW.test(line)) {
      return
    }

    const lineNumber = index + 1
    const cells = splitCells(line)
    if (cells.length > limits.maxColumns) {
      violations.push({
        line: lineNumber,
        kind: 'columns',
        detail: `${cells.length} columns (max ${limits.maxColumns})`,
      })
    }
    for (const raw of cells) {
      const text = cellText(raw)
      const width = displayWidth(text)
      if (width > limits.maxCellWidth) {
        violations.push({
          line: lineNumber,
          kind: 'width',
          detail: `cell width ${width} > ${limits.maxCellWidth}: "${text}"`,
        })
      }
    }
  })

  return violations
}
