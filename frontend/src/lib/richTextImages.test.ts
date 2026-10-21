import { describe, it, expect } from 'vitest'
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

  it('keeps a query string out of the id and stores the bare path', () => {
    const out = normalizeForStorage(`<img src="/api/files/${ID}/content?w=100">`)
    expect(out).toContain(`data-file-id="${ID}"`)
    expect(out).toContain(`src="/api/files/${ID}/content"`)
  })

  it('relativizes an image that already carries a valid data-file-id', () => {
    const out = normalizeForStorage(`<img src="http://localhost:5221/api/files/${ID}/content" data-file-id="${ID}">`)
    expect(out).toContain(`src="/api/files/${ID}/content"`)
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
