import { describe, it, expect, afterEach } from 'vitest'
import { isEditorImage } from './richTextImageActions'

// Mirrors the real DOM shape Task 2's image node view produces (verified against
// @tiptap/core's ResizableNodeView source this session): a [data-resize-container] div wraps a
// [data-resize-wrapper] div, which holds the <img> itself plus its resize-handle divs as siblings
// of the image -- not descendants of it.
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

  // The node view wraps every image in two container divs. closest() would find the <img> from
  // there too, but a click on the wrapper's own padding is not a click on the image -- only the
  // exact event target matters, which is why this function must NOT walk up via closest().
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

  // Images win over tables: an <img> inside a <td> is still an image click, not a table click.
  // Task 5's routing relies on this predicate-level fact to decide image-before-table priority.
  it('returns the <img> even when it sits inside a table cell', () => {
    const { root, tableImg } = build()
    expect(isEditorImage(tableImg, root)).toBe(tableImg)
  })
})
