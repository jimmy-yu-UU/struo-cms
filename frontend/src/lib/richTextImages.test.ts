import { describe, it, expect } from 'vitest'
import {
  fileContentPath, fileContentDisplayUrl, absolutizeImageSrc, relativizeImageSrc,
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

  it('relativizes img src back to the stored path', () => {
    const html = `<img src="${fileContentDisplayUrl('abc')}" data-file-id="abc" alt="a">`
    const out = relativizeImageSrc(html)
    expect(out).toContain('src="/api/files/abc/content"')
  })

  it('leaves images without data-file-id untouched', () => {
    const html = '<img src="http://x/y.png" alt="a">'
    expect(relativizeImageSrc(html)).toContain('src="http://x/y.png"')
  })
})
