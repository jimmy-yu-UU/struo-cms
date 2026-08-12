// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const SRC = fileURLToPath(new URL('../src/', import.meta.url))
const MEDIA_DIR = join(SRC, 'components/media')
const MEDIA_VIEW = join(SRC, 'views/MediaLibraryView.vue')

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    return statSync(full).isDirectory() ? walk(full) : (/\.vue$/.test(entry) ? [full] : [])
  })
}

// Scoped to the media library's own components and view -- deliberately NOT src/components/common/.
// common/PageHeader.vue keeps one var(--legacy-muted) on purpose: it (and the global .caption rule
// in theme.css it consumes) is owned by S9, the plan that deletes the whole --legacy-* block, not by
// this slice's conversion. Sweeping common/ here would make the guard fail the moment an unrelated,
// out-of-scope file changed, rather than when this slice regressed.
const files = [...walk(MEDIA_DIR), MEDIA_VIEW]

describe('media library legacy token conversion', () => {
  it('no file under components/media/ or MediaLibraryView.vue consumes a --legacy- token', () => {
    const offenders = files.filter((f) => /var\(--legacy-/.test(readFileSync(f, 'utf8')))
    expect(offenders.map((f) => f.replace(SRC, ''))).toEqual([])
  })
})
