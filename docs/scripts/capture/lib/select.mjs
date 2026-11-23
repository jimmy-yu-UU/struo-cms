// Which chapter files a run writes. The run itself never changes with the selection: every group
// runs in its fixed order, so a partial run produces the same data as a full one.
import { declaredByFile } from './check.mjs'
import { CaptureError } from './errors.mjs'

// option: "10,11" or undefined (every file with a capture). Duplicates collapse and the result
// follows the order in which the run first uses each file, not the order of the option.
export function selectFiles(option, groups) {
  const files = [...declaredByFile(groups).keys()]
  if (!option) return files
  const wanted = new Set()
  for (const raw of option.split(',')) {
    const number = raw.trim()
    if (!files.some((file) => file.startsWith(`${number}-`))) throw new CaptureError(`no scenario for chapter ${number}`)
    wanted.add(number)
  }
  return files.filter((file) => wanted.has(file.slice(0, file.indexOf('-'))))
}

// The closing line of a run: how many files changed and which chapter files were selected.
export function summarize({ write, files, only }) {
  const changed = files.filter((file) => file.changed).length
  return `${write ? 'wrote' : 'would write'} ${changed} files (${only.join(', ')})`
}
