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
    return statSync(full).isDirectory() ? walk(full) : (/\.(vue|css)$/.test(entry) ? [full] : [])
  })
}

// Scoped to the media library's own components and view -- deliberately NOT src/components/common/.
// common/PageHeader.vue keeps one var(--legacy-muted) on purpose, and it is not the only survivor:
// components/rbac/, components/revisions/, LoginView.vue, SettingsView.vue and theme.css itself
// still consume --legacy-* tokens too. Converting those is separate, unstarted work; sweeping them
// here would make this guard fail the moment someone touched a file this task never owned, rather
// than when the media library itself regressed. This guard overlaps tests/legacyTokens.test.ts and
// becomes redundant once every --legacy-* consumer is gone and theme.css's --legacy-* block is
// deleted -- delete it at that point instead of keeping a check with nothing left to catch.
const files = [...walk(MEDIA_DIR), MEDIA_VIEW]

describe('media library legacy token conversion', () => {
  it('no file under components/media/ or MediaLibraryView.vue consumes a --legacy- token', () => {
    const offenders = files.filter((f) => /var\(--legacy-/.test(readFileSync(f, 'utf8')))
    expect(offenders.map((f) => f.replace(SRC, ''))).toEqual([])
  })
})
