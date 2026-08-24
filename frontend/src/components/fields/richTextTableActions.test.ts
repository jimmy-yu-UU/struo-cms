import { describe, it, expect, afterEach } from 'vitest'
import { isInEditorTable, TABLE_SIZE_MIN, TABLE_SIZE_MAX } from './richTextTableActions'

function build(): { root: HTMLElement; cell: HTMLElement; para: HTMLElement; outsideCell: HTMLElement } {
  const host = document.createElement('div')
  host.innerHTML =
    '<div class="pm"><table><tbody><tr><td id="cell">in</td></tr></tbody></table><p id="para">out</p></div>' +
    '<table><tbody><tr><td id="outside">elsewhere</td></tr></tbody></table>'
  document.body.appendChild(host)
  return {
    root: host.querySelector('.pm') as HTMLElement,
    cell: host.querySelector('#cell') as HTMLElement,
    para: host.querySelector('#para') as HTMLElement,
    outsideCell: host.querySelector('#outside') as HTMLElement,
  }
}

afterEach(() => { document.body.innerHTML = '' })

describe('isInEditorTable', () => {
  it('is true for a node inside a table inside the editor root', () => {
    const { root, cell } = build()
    expect(isInEditorTable(cell, root)).toBe(true)
  })

  it('is false for a node inside the editor root but outside any table', () => {
    const { root, para } = build()
    expect(isInEditorTable(para, root)).toBe(false)
  })

  // A table elsewhere on the page must not arm the editor's own context menu.
  it('is false for a table that lives outside the editor root', () => {
    const { root, outsideCell } = build()
    expect(isInEditorTable(outsideCell, root)).toBe(false)
  })

  it('is false for a null target', () => {
    const { root } = build()
    expect(isInEditorTable(null, root)).toBe(false)
  })
})

describe('table size bounds', () => {
  it('exposes the shared 1-20 bounds', () => {
    expect(TABLE_SIZE_MIN).toBe(1)
    expect(TABLE_SIZE_MAX).toBe(20)
  })
})
