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
// scripts/capture/lib/markers.mjs uses fenceOpener and closesFence directly, so the capture markers
// follow the same rule as the guards.
//
// Splitting the input into lines (on `\r?\n`) stays the caller's job: each
// caller already has its own reasons to hold onto that array.
//
// fencedLines(lines) returns a boolean per input line: true for a fence
// opener, every line inside it, and its closer. An unclosed fence marks
// every remaining line true — the same thing a renderer does.
const FENCE_LINE = /^ {0,3}(`{3,}|~{3,})/

// The fence run of an opener line (for example "```" or "~~~~"), or null when the line opens nothing.
export function fenceOpener(line) {
  const match = FENCE_LINE.exec(line)
  return match ? match[1] : null
}

// Whether `line` closes a fence opened with the run `opener`: same character, at least as long.
export function closesFence(opener, line) {
  const run = fenceOpener(line)
  return run !== null && run.startsWith(opener[0]) && run.length >= opener.length
}

export function fencedLines(lines) {
  const fenced = new Array(lines.length).fill(false)
  let open = null // the fence run of the open fence, or null
  lines.forEach((line, index) => {
    if (open) {
      fenced[index] = true
      if (closesFence(open, line)) open = null
      return
    }
    const opener = fenceOpener(line)
    if (opener) {
      fenced[index] = true
      open = opener
    }
  })
  return fenced
}
