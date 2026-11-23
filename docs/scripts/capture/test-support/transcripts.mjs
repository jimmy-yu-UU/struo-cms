// Finds the fenced blocks of a chapter that look like an HTTP or command transcript and are not
// directly under a capture marker. The fence rule is the shared one (scripts/lib/fences.mjs), so
// indented and tilde fences are seen like any other.
import { closesFence, fenceOpener } from '../../lib/fences.mjs'

const MARKER = /^<!-- capture: [\w.-]+ -->$/
const TRANSCRIPT_LINE = /^\s*(\$ |HTTP_STATUS:)/

// The 1-based line numbers of the opening fences of the unmarked transcript blocks in `text`.
export function unmarkedTranscriptBlocks(text) {
  const lines = text.split(/\r?\n/)
  const found = []
  let index = 0
  while (index < lines.length) {
    const opener = fenceOpener(lines[index])
    if (!opener) {
      index += 1
      continue
    }
    let close = index + 1
    while (close < lines.length && !closesFence(opener, lines[close])) close += 1
    const marked = index > 0 && MARKER.test(lines[index - 1])
    const body = lines.slice(index + 1, close)
    if (!marked && body.some((line) => TRANSCRIPT_LINE.test(line))) found.push(index + 1)
    index = close + 1
  }
  return found
}
