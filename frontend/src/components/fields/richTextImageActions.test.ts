import { describe, it, expect, afterEach } from 'vitest'
import { isEditorImage } from './richTextImageActions'

// Mirrors the real DOM shape the image node view produces: a [data-resize-container] div wraps a
// [data-resize-wrapper] div holding the <img> plus its resize handles as siblings of the image.
function build(): {
  root: HTMLElement
  img: HTMLImageElement
  container: HTMLElement
  wrapper: HTMLElement
  tableImg: HTMLImageElement
  outsideImg: HTMLImageElement
} {
  const host = document.createElement('div')
  host.innerHTML =
    '<div class="pm">' +
    '<div data-resize-container>' +
    '<div data-resize-wrapper><img id="img1" src="a.png"><div data-resize-handle="bottom-right"></div></div>' +
    '</div>' +
    '<table><tbody><tr><td>' +
    '<div data-resize-container>' +
    '<div data-resize-wrapper><img id="img2" src="b.png"></div>' +
    '</div>' +
    '</td></tr></tbody></table>' +
    '</div>' +
    '<img id="outside" src="c.png">'
  document.body.appendChild(host)
  return {
    root: host.querySelector('.pm') as HTMLElement,
    img: host.querySelector('#img1') as HTMLImageElement,
    container: host.querySelector('[data-resize-container]') as HTMLElement,
    wrapper: host.querySelector('[data-resize-wrapper]') as HTMLElement,
    tableImg: host.querySelector('#img2') as HTMLImageElement,
    outsideImg: host.querySelector('#outside') as HTMLImageElement,
  }
}

afterEach(() => { document.body.innerHTML = '' })

describe('isEditorImage', () => {
  it('returns the <img> itself when the click landed on it', () => {
    const { root, img } = build()
    expect(isEditorImage(img, root)).toBe(img)
  })

  // Fails if this predicate is rewritten with closest(): the node view's wrapper divs would then
  // make a click beside the image count as a click on it.
  it('returns null for a click on the node view\'s wrapping container, not the <img> itself', () => {
    const { root, container, wrapper } = build()
    expect(isEditorImage(container, root)).toBeNull()
    expect(isEditorImage(wrapper, root)).toBeNull()
  })

  it('returns null for an <img> that lives outside the editor root', () => {
    const { root, outsideImg } = build()
    expect(isEditorImage(outsideImg, root)).toBeNull()
  })

  it('returns null for a non-Node target', () => {
    const { root } = build()
    expect(isEditorImage({} as EventTarget, root)).toBeNull()
    expect(isEditorImage(null, root)).toBeNull()
  })

  // Images win over tables: an <img> inside a <td> is still an image click. RichTextInput's
  // context-menu routing depends on this predicate matching there.
  it('returns the <img> even when it sits inside a table cell', () => {
    const { root, tableImg } = build()
    expect(isEditorImage(tableImg, root)).toBe(tableImg)
  })
})
