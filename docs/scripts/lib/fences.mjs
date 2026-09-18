// Shared CommonMark fenced-code-block rule: a fence opener is 3 or more
// backticks or tildes, indented up to 3 spaces; its closer must reuse the
// same character and be at least as long. Two guard scripts each need to know
// which lines are inside a fence — check-rendered-chapters.mjs so a bare
// `{{ }}` inside a code sample is never mistaken for prose, and
// markdown-tables.mjs so a table shown as a code sample is never measured —
// so the rule lives here once. Two copies of the same rule are exactly how a
// divergence between them turns into a false positive or false negative that
// gets a guard deleted; one copy can't diverge from itself.
//
// Splitting the input into lines (on `\r?\n`) stays the caller's job: both
// callers already have their own reasons to hold onto that array.
//
// fencedLines(lines) returns a boolean per input line: true for a fence
// opener, every line inside it, and its closer. An unclosed fence marks
// every remaining line true — the same thing a renderer does.
const FENCE_LINE = /^ {0,3}(`{3,}|~{3,})/

export function fencedLines(lines) {
  const fenced = new Array(lines.length).fill(false)
  let fence = null // { char, length } of the open fence, or null
  lines.forEach((line, index) => {
    const opener = FENCE_LINE.exec(line)
    if (fence) {
      fenced[index] = true
      if (opener && opener[1].startsWith(fence.char) && opener[1].length >= fence.length) {
        fence = null
      }
      return
    }
    if (opener) {
      fenced[index] = true
      fence = { char: opener[1][0], length: opener[1].length }
    }
  })
  return fenced
}
