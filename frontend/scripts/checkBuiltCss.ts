// Runs after `vite build` (see the "build" script in package.json). The admin has no `not-allowed`
// cursor: disabled controls show the default cursor. This reads the built stylesheets and fails
// when a rule still sets that cursor.
import { existsSync, readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

export type CssFile = { name: string; css: string }

const NOT_ALLOWED = /cursor\s*:\s*not-allowed/i
const WHITESPACE = /\s+/g
const ASSETS_DIR = fileURLToPath(new URL('../dist/assets', import.meta.url))

// vue-sonner's stylesheet sets this cursor on a disabled toast button. tokens.css overrides that
// rule with a more specific selector, so the declaration is present in the build but never wins.
const OVERRIDDEN_BY_TOKENS = new Set(['[data-sonner-toast][data-styled=true] [data-disabled=true]'])

const normalizeSelector = (selector: string): string => selector.replaceAll(WHITESPACE, ' ').trim()

// Selectors of the rules that declare the not-allowed cursor. Each chunk ends at a closing brace;
// the text after its last opening brace is the declaration block, and the selector sits between
// the opening brace before that one (an enclosing @media or @layer) and it.
export function findNotAllowedRules(css: string): string[] {
  return css
    .split('}')
    .flatMap((chunk) => {
      const open = chunk.lastIndexOf('{')
      if (open < 0 || !NOT_ALLOWED.test(chunk.slice(open + 1))) return []
      const selectorStart = open === 0 ? 0 : chunk.lastIndexOf('{', open - 1) + 1
      return [normalizeSelector(chunk.slice(selectorStart, open))]
    })
}

export function checkBuiltCss(files: readonly CssFile[], assetsDir: string): string[] {
  if (files.length === 0) {
    return [`No stylesheet found in ${assetsDir}. Run \`vite build\` first; this check reads its output.`]
  }
  return files.flatMap(({ name, css }) => {
    const offenders = findNotAllowedRules(css).filter((selector) => !OVERRIDDEN_BY_TOKENS.has(selector))
    return offenders.length === 0
      ? []
      : [
          `${name} sets \`cursor: not-allowed\` in: ${offenders.join(', ')}. ` +
            'Disabled controls use the default cursor; remove the utility from first-party code, ' +
            'or list the variant in the `@source not inline(...)` line of src/assets/tokens.css. ' +
            'For a rule of a third-party stylesheet, override it in tokens.css and list its selector ' +
            'in OVERRIDDEN_BY_TOKENS.',
        ]
  })
}

function readBuiltCss(dir: string): CssFile[] {
  if (!existsSync(dir)) return []
  return readdirSync(dir)
    .filter((name) => name.endsWith('.css'))
    .map((name) => ({ name, css: readFileSync(join(dir, name), 'utf8') }))
}

function main(): void {
  const errors = checkBuiltCss(readBuiltCss(ASSETS_DIR), ASSETS_DIR)
  if (errors.length > 0) {
    for (const error of errors) console.error(error)
    process.exitCode = 1
  }
}

if (import.meta.main) main()
