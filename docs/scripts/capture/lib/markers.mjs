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
// the scan tracks fences with the same CommonMark rule as ../../lib/fences.mjs: an opener is three
// or more backticks or tildes indented up to three spaces; the closer reuses the character and is
// at least as long. markers.test.mjs checks the two against each other.
const MARKER = /^<!-- capture: ([A-Za-z0-9._-]+) -->$/
const FENCE = /^ {0,3}(`{3,}|~{3,})/

export class MarkerError extends Error {}

function splitLines(text) {
  const eol = text.includes('\r\n') ? '\r\n' : '\n'
  return { lines: text.split(/\r?\n/), eol }
}

function closes(opener, line) {
  const match = FENCE.exec(line)
  return match !== null && match[1][0] === opener[0] && match[1].length >= opener.length
}

function where(file, line) {
  return file ? `${file}:${line + 1}: ` : `line ${line + 1}: `
}

function scan(lines, file) {
  const blocks = []
  let index = 0
  while (index < lines.length) {
    const marker = MARKER.exec(lines[index])
    const opener = FENCE.exec(lines[index])
    if (opener) {
      // A fence outside any marker: skip to its closer so its content is never read as markers.
      let end = index + 1
      while (end < lines.length && !closes(opener[1], lines[end])) end += 1
      index = end + 1
      continue
    }
    if (!marker) {
      index += 1
      continue
    }
    const id = marker[1]
    const fence = index + 1 < lines.length ? FENCE.exec(lines[index + 1]) : null
    if (!fence) {
      throw new MarkerError(`${where(file, index)}marker '${id}' is not directly above a fence`)
    }
    let close = index + 2
    while (close < lines.length && !closes(fence[1], lines[close])) close += 1
    if (close >= lines.length) {
      throw new MarkerError(`${where(file, index)}marker '${id}': the fence is never closed`)
    }
    if (blocks.some((block) => block.id === id)) {
      throw new MarkerError(`${where(file, index)}marker '${id}' appears twice`)
    }
    blocks.push({ id, markerLine: index, openLine: index + 1, closeLine: close })
    index = close + 1
  }
  return blocks
}

// Zero-based line numbers of each block's marker, opening fence and closing fence.
export function findBlocks(text, { file } = {}) {
  return scan(splitLines(text).lines, file)
}

// The opening fence, the content and the closing fence of one block, or null when the id has no
// marker. This is the span two languages must hold byte for byte.
export function blockRegion(text, id, { file } = {}) {
  const { lines } = splitLines(text)
  const block = scan(lines, file).find((candidate) => candidate.id === id)
  return block ? lines.slice(block.openLine, block.closeLine + 1) : null
}

// Returns the text with the lines between the fences of block `id` replaced by `content`.
// The file's own line ending is kept.
export function replaceBlock(text, id, content, { file } = {}) {
  const { lines, eol } = splitLines(text)
  const block = scan(lines, file).find((candidate) => candidate.id === id)
  if (!block) throw new MarkerError(`${file ? `${file}: ` : ''}no marker '${id}'`)
  const opener = FENCE.exec(lines[block.openLine])[1]
  const replacement = content.split('\n')
  if (replacement.some((line) => closes(opener, line))) {
    throw new MarkerError(`${id}: a line of the captured text would close the fence`)
  }
  return [...lines.slice(0, block.openLine + 1), ...replacement, ...lines.slice(block.closeLine)].join(eol)
}
