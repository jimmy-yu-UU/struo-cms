// The manual renders in VitePress's default content column (about 688px).
// A table wider than that scrolls horizontally, which the maintainer ruled
// unacceptable on 2026-09-10: a wide cell is as unreadable as a long paragraph.
// Nothing in `vitepress build` measures this, so the guard lives here.
//
// The check is deliberately mechanical and source-side. It finds tables the
// way markdown-it (VitePress's renderer) does: a delimiter row anchors a
// table only when the header line above it contains a pipe, so a table
// missing its outer pipes or indented inside a list item is still found —
// neither hides a table from markdown-it, so neither hides one here — while a
// setext heading underline or YAML frontmatter's `---` never anchors one,
// because their header line has no pipe. It measures the text a reader will
// see — inline code, link targets and emphasis markers stripped — with CJK
// characters counted double, because at the manual's font size one CJK glyph
// takes about the space of two Latin ones. There is no per-table exemption on
// purpose: a table that cannot fit must be rewritten as a list or a section,
// not waved through.

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
// least as long. Tables in fenced code samples must never be measured — that
// false positive is exactly what would get this guard deleted later.
const FENCE_LINE = /^ {0,3}(`{3,}|~{3,})/

// A delimiter row, after stripping leading whitespace and one optional
// leading/trailing pipe: one or more `-`-only cells (each optionally
// colon-anchored for alignment), separated by `|`. This is the anchor for
// table detection, but only when the header line above it also contains a
// pipe — markdown-it's table rule requires that too, which is what keeps a
// setext heading underline (`prose` / `---`) or YAML frontmatter's `---`
// fences from being mistaken for one. The header is whatever non-blank,
// non-fence line sits immediately above the delimiter row, and the body is
// the consecutive lines that follow.
const DELIMITER_ROW = /^:?-+:?$/
const HAS_UNESCAPED_PIPE = /(?<!\\)\|/

// Strip leading whitespace, one leading `|` and one trailing `|` (after
// trailing whitespace), then split on unescaped `|`. A row with no pipes at
// all is still one cell — splitting on a separator that is not present
// yields the whole (trimmed) row as its single element.
function rowCells(line) {
  let row = line.replace(/^\s+/, '')
  if (row.startsWith('|')) {
    row = row.slice(1)
  }
  row = row.replace(/\s+$/, '')
  if (row.endsWith('|')) {
    row = row.slice(0, -1)
  }
  return row.split(/(?<!\\)\|/)
}

function isDelimiterRow(line) {
  const cells = rowCells(line)
  return cells.length > 0 && cells.every((cell) => DELIMITER_ROW.test(cell.trim()))
}

function isBlank(line) {
  return line.trim() === ''
}

export function checkTables(markdown, limits) {
  const lines = markdown.split(/\r?\n/)
  const violations = []
  const fenceAt = new Array(lines.length).fill(false)

  // Pre-compute which lines sit inside a fence, so header/delimiter/body
  // scanning below never has to track fence state itself.
  let fence = null
  lines.forEach((line, index) => {
    const opener = FENCE_LINE.exec(line)
    if (fence) {
      fenceAt[index] = true
      if (opener && opener[1].startsWith(fence.char) && opener[1].length >= fence.length) {
        fence = null
      }
      return
    }
    if (opener) {
      fenceAt[index] = true
      fence = { char: opener[1][0], length: opener[1].length }
    }
  })

  function measureRow(line, lineNumber) {
    const cells = rowCells(line)
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
  }

  let index = 0
  while (index < lines.length) {
    const line = lines[index]
    if (fenceAt[index] || isBlank(line) || !isDelimiterRow(line)) {
      index += 1
      continue
    }

    const headerIndex = index - 1
    const headerLine = headerIndex >= 0 ? lines[headerIndex] : undefined
    if (
      headerIndex < 0 ||
      fenceAt[headerIndex] ||
      isBlank(headerLine) ||
      !HAS_UNESCAPED_PIPE.test(headerLine)
    ) {
      index += 1
      continue
    }

    measureRow(headerLine, headerIndex + 1)

    let bodyIndex = index + 1
    while (bodyIndex < lines.length && !fenceAt[bodyIndex] && !isBlank(lines[bodyIndex])) {
      measureRow(lines[bodyIndex], bodyIndex + 1)
      bodyIndex += 1
    }

    index = bodyIndex
  }

  return violations
}
