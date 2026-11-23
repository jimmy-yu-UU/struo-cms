// Capture markers. A transcript block is addressed by an HTML comment on the line directly above
// its opening fence:
//
//   <!-- capture: <id> -->
//   ```text
//   ...the lines the tool owns...
//   ```
//
// The tool replaces only the lines between the fences. VitePress drops the comment from the
// rendered page, so the marker costs the reader nothing.
//
// A marker-looking line inside a fenced block is content (the manual shows markdown samples), so
// the scan tracks fences with the shared rule in ../../lib/fences.mjs. Outside a fence, a line that
// holds a capture comment in any other form (indented, spaced differently, mid-line) is an error:
// a marker the tool would silently ignore is a block nobody updates. One shape is exempt because it
// is text about the marker, not a marker: an inline code span. The manual writes code in fences, so
// an indented comment is a near miss like any other.
import { closesFence, fenceOpener } from '../../lib/fences.mjs'

const MARKER = /^<!-- capture: ([\w.-]+) -->$/
const NEAR_MISS = /<!--\s*capture/i
const INLINE_CODE = /``[^`]+``|`[^`]+`/g

export class MarkerError extends Error {}

// Lines without their terminators, and each line's own terminator ('' for a last line without one).
function split(text) {
  const parts = text.split(/(\r\n|\n)/)
  const lines = []
  const eols = []
  for (let i = 0; i < parts.length; i += 2) {
    lines.push(parts[i])
    eols.push(parts[i + 1] ?? '')
  }
  return { lines, eols }
}

function where(file, line) {
  return file ? `${file}:${line + 1}: ` : `line ${line + 1}: `
}

function fail(file, line, message) {
  return new MarkerError(`${where(file, line)}${message}`)
}

function findCloser(lines, from, opener) {
  let close = from
  while (close < lines.length && !closesFence(opener, lines[close])) close += 1
  return close
}


function rejectNearMiss(line, file, index) {
  if (NEAR_MISS.test(line.replace(INLINE_CODE, ''))) {
    throw fail(
      file,
      index,
      "near-miss capture marker; the form is '<!-- capture: <id> -->' at column 0, directly above a fence",
    )
  }
}

// The block whose marker sits on line `index`, or an error when no closed fence follows it.
function readBlock(lines, index, id, file) {
  const fence = index + 1 < lines.length ? fenceOpener(lines[index + 1]) : null
  if (!fence) throw fail(file, index, `marker '${id}' is not directly above a fence`)
  const close = findCloser(lines, index + 2, fence)
  if (close >= lines.length) throw fail(file, index, `marker '${id}': the fence is never closed`)
  return { id, markerLine: index, openLine: index + 1, closeLine: close }
}

function scan(lines, file) {
  const blocks = []
  let index = 0
  while (index < lines.length) {
    const line = lines[index]
    const opener = fenceOpener(line)
    if (opener) {
      // A fence outside any marker: skip to its closer so its content is never read as markers.
      index = findCloser(lines, index + 1, opener) + 1
      continue
    }
    const marker = MARKER.exec(line)
    if (!marker) {
      rejectNearMiss(line, file, index)
      index += 1
      continue
    }
    const block = readBlock(lines, index, marker[1], file)
    if (blocks.some((candidate) => candidate.id === block.id)) {
      throw fail(file, index, `marker '${block.id}' appears twice`)
    }
    blocks.push(block)
    index = block.closeLine + 1
  }
  return blocks
}

// Zero-based line numbers of each block's marker, opening fence and closing fence.
export function findBlocks(text, { file } = {}) {
  return scan(split(text).lines, file)
}

// The opening fence, the content and the closing fence of one block, or null when the id has no
// marker. This is the span two languages must hold byte for byte.
export function blockRegion(text, id, { file } = {}) {
  const { lines } = split(text)
  const block = scan(lines, file).find((candidate) => candidate.id === id)
  return block ? lines.slice(block.openLine, block.closeLine + 1) : null
}

// Returns the text with the lines between the fences of block `id` replaced by `content`. The new
// lines end with the file's first line ending; every other line keeps its own.
export function replaceBlock(text, id, content, { file } = {}) {
  if (content.includes('\r')) throw new MarkerError(`${id}: the captured text contains a carriage return`)
  const { lines, eols } = split(text)
  const block = scan(lines, file).find((candidate) => candidate.id === id)
  const prefix = file ? `${file}: ` : ''
  if (!block) throw new MarkerError(`${prefix}no marker '${id}'`)
  const opener = fenceOpener(lines[block.openLine])
  const replacement = content.split('\n')
  if (replacement.some((line) => closesFence(opener, line))) {
    throw new MarkerError(`${id}: a line of the captured text would close the fence`)
  }
  const eol = eols.find((candidate) => candidate !== '') ?? '\n'
  const keep = (from, to) => lines.slice(from, to).map((line, offset) => line + eols[from + offset])
  const created = replacement.map((line) => line + eol)
  return [...keep(0, block.openLine + 1), ...created, ...keep(block.closeLine, lines.length)].join('')
}
