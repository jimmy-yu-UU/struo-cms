// @vitest-environment node
import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

const FILE = fileURLToPath(new URL('../src/components/media/FileThumbnail.vue', import.meta.url))

// jsdom never applies a Vue SFC's <style scoped> block during a unit test (there is no `css: true`
// in vite.config.ts), so no assertion made against a mounted FileThumbnail's computed style can
// prove the sm-size sizing rule still exists -- only reading the source text can. Same approach as
// tests/legacyTokens.test.ts, which already reads a .vue file's own <style> block as plain text to
// assert on CSS that jsdom will never apply.
describe('FileThumbnail sm-size rule', () => {
  it('still declares the [data-size="sm"] sizing rule', () => {
    const css = readFileSync(FILE, 'utf8')
    expect(css).toMatch(/\.file-thumb\[data-size=['"]sm['"]\]/)
  })
})
