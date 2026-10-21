import { describe, it, expect, vi } from 'vitest'
import {
  fileContentPath, fileContentDisplayUrl, absolutizeImageSrc, normalizeForStorage,
} from './richTextImages'

describe('richTextImages', () => {
  it('builds the stored relative path', () => {
    expect(fileContentPath('abc')).toBe('/api/files/abc/content')
  })

  it('builds a display url from the api base', () => {
    // default base is '/api' in tests
    expect(fileContentDisplayUrl('abc')).toBe('/api/files/abc/content')
  })

  it('absolutizes img src from data-file-id', () => {
    const html = '<p>x</p><img src="/api/files/abc/content" data-file-id="abc" alt="a">'
    const out = absolutizeImageSrc(html)
    expect(out).toContain('data-file-id="abc"')
    expect(out).toContain(`src="${fileContentDisplayUrl('abc')}"`)
  })

  const ID = '3f2b8c1e-9a4d-4e6f-8b7a-1c2d3e4f5a6b'

  it('relativizes a managed image and tags its file id', () => {
    const out = normalizeForStorage(`<img src="${fileContentDisplayUrl(ID)}" alt="a">`)
    expect(out).toContain(`src="/api/files/${ID}/content"`)
    expect(out).toContain(`data-file-id="${ID}"`)
  })

  it('tags a managed image that has a query string', () => {
    const out = normalizeForStorage(`<img src="/api/files/${ID}/content?w=100">`)
    expect(out).toContain(`data-file-id="${ID}"`)
  })

  it('trusts a data-file-id only on an image with a managed or empty src', () => {
    const empty = normalizeForStorage(`<img data-file-id="${ID}">`)
    expect(empty).toContain(`src="/api/files/${ID}/content"`)
    const foreign = '<img src="https://other.example/api/files/' + ID + '/content" data-file-id="' + ID + '">'
    expect(normalizeForStorage(foreign)).toBe(foreign)
    const external = `<img src="https://cdn.example.com/x.png" data-file-id="${ID}">`
    expect(normalizeForStorage(external)).toBe(external)
  })

  it('keeps the query of a managed url when storing', () => {
    const out = normalizeForStorage(`<img src="${fileContentDisplayUrl(ID)}?width=300&amp;format=webp">`)
    expect(out).toContain(`src="/api/files/${ID}/content?width=300&amp;format=webp"`)
  })

  it('keeps the query of a managed path when loading', () => {
    const out = absolutizeImageSrc(`<img src="/api/files/${ID}/content?width=300" data-file-id="${ID}">`)
    expect(out).toContain(`src="${fileContentDisplayUrl(ID)}?width=300"`)
  })

  it('absolutizes only managed or empty-src images that carry a guid id', () => {
    const external = `<img src="https://cdn.example.com/x.png" data-file-id="${ID}">`
    expect(absolutizeImageSrc(external)).toBe(external)
    const bad = '<img src="https://cdn.example.com/x.png" data-file-id="nope">'
    expect(absolutizeImageSrc(bad)).toBe(bad)
    const nonGuidManaged = '<img src="/api/files/abc/content" data-file-id="abc">'
    expect(absolutizeImageSrc(nonGuidManaged)).toBe(nonGuidManaged)
    expect(absolutizeImageSrc(`<img data-file-id="${ID}">`)).toContain(`src="${fileContentDisplayUrl(ID)}"`)
  })

  it('round-trips load then save for managed and external images', () => {
    const external = `<img src="https://cdn.example.com/x.png" data-file-id="${ID}">`
    expect(normalizeForStorage(absolutizeImageSrc(external))).toBe(external)
    const managed = `<img src="/api/files/${ID}/content?width=300" data-file-id="${ID}">`
    expect(normalizeForStorage(absolutizeImageSrc(managed))).toBe(managed)
  })

  it('leaves an external image whose url contains /files/x/content untouched', () => {
    const html = '<p>x</p><img src="https://cdn.example.com/files/abc/content" alt="a">'
    const out = normalizeForStorage(html)
    expect(out).toContain('src="https://cdn.example.com/files/abc/content"')
    expect(out).not.toContain('data-file-id')
  })

  it('leaves an external image with a guid in a foreign host untouched', () => {
    const html = `<img src="https://cdn.example.com/api/files/${ID}/content">`
    const out = normalizeForStorage(html)
    expect(out).toContain(`src="https://cdn.example.com/api/files/${ID}/content"`)
    expect(out).not.toContain('data-file-id')
  })

  it('leaves a managed-looking path with a non-guid id untouched', () => {
    const out = normalizeForStorage('<img src="/api/files/abc/content">')
    expect(out).toContain('src="/api/files/abc/content"')
    expect(out).not.toContain('data-file-id')
  })

  it('ignores a non-guid data-file-id on an external image', () => {
    const out = normalizeForStorage('<img src="http://x/y.png" data-file-id="nope">')
    expect(out).toContain('src="http://x/y.png"')
  })

  it('returns html without images as the same string', () => {
    const html = '<p>a<br>b &amp; c</p><ul><li>x</li></ul>'
    expect(normalizeForStorage(html)).toBe(html)
    expect(normalizeForStorage('')).toBe('')
  })

  it('leaves images without data-file-id untouched', () => {
    const html = '<img src="http://x/y.png" alt="a">'
    expect(normalizeForStorage(html)).toContain('src="http://x/y.png"')
  })
})

describe('richTextImages with an absolute api base', () => {
  const ID = '3f2b8c1e-9a4d-4e6f-8b7a-1c2d3e4f5a6b'

  it('treats only that base and the stored path as managed', async () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.com/api')
    vi.resetModules()
    const lib = await import('./richTextImages')
    const mine = `<img src="https://api.example.com/api/files/${ID}/content?w=1">`
    expect(lib.normalizeForStorage(mine)).toContain(`src="/api/files/${ID}/content?w=1"`)
    const other = `<img src="https://other.example.com/api/files/${ID}/content">`
    expect(lib.normalizeForStorage(other)).toBe(other)
    expect(lib.absolutizeImageSrc(`<img src="/api/files/${ID}/content" data-file-id="${ID}">`))
      .toContain(`src="https://api.example.com/api/files/${ID}/content"`)
    vi.unstubAllEnvs()
    vi.resetModules()
  })
})
