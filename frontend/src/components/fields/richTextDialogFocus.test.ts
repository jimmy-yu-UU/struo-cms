import { describe, it, expect, vi } from 'vitest'
import { nextTick } from 'vue'
import { createDialogFocus } from './richTextDialogFocus'

function attach(): HTMLButtonElement {
  const el = document.createElement('button')
  document.body.appendChild(el)
  return el
}

describe('createDialogFocus', () => {
  it('restores focus to the element that had it when the dialog opened', async () => {
    const focusEditor = vi.fn()
    const focus = createDialogFocus(focusEditor)
    const el = attach()
    el.focus()

    focus.blurActiveElementBeforeDialog()
    expect(document.activeElement).toBe(document.body)

    focus.refocusAfterDialogCancel()
    await nextTick()
    expect(document.activeElement).toBe(el)
    expect(focusEditor).not.toHaveBeenCalled()
    el.remove()
  })

  it('falls back to the editor when the captured element has left the document', async () => {
    const focusEditor = vi.fn()
    const focus = createDialogFocus(focusEditor)
    const el = attach()
    el.focus()

    focus.blurActiveElementBeforeDialog()
    el.remove()
    focus.refocusAfterDialogCancel()
    await nextTick()
    expect(focusEditor).toHaveBeenCalledTimes(1)
  })

  it('captures nothing when only body was focused', async () => {
    const focusEditor = vi.fn()
    const focus = createDialogFocus(focusEditor)
    focus.blurActiveElementBeforeDialog()
    focus.refocusAfterDialogCancel()
    await nextTick()
    expect(focusEditor).not.toHaveBeenCalled()
  })

  it('prefers an explicit restore over the element that had focus', async () => {
    const focusEditor = vi.fn()
    const restore = vi.fn()
    const focus = createDialogFocus(focusEditor)
    const el = attach()
    el.focus()

    focus.blurActiveElementBeforeDialog(restore)
    focus.refocusAfterDialogCancel()
    await nextTick()
    expect(restore).toHaveBeenCalledTimes(1)
    expect(document.activeElement).toBe(document.body)
    el.remove()
  })

  it('clearRestore cancels a pending restore', async () => {
    const focusEditor = vi.fn()
    const focus = createDialogFocus(focusEditor)
    const el = attach()
    el.focus()

    focus.blurActiveElementBeforeDialog()
    focus.clearRestore()
    focus.refocusAfterDialogCancel()
    await nextTick()
    expect(document.activeElement).toBe(document.body)
    expect(focusEditor).not.toHaveBeenCalled()
    el.remove()
  })

  it('defers the restore past the current tick', () => {
    const focus = createDialogFocus(vi.fn())
    const el = attach()
    el.focus()
    focus.blurActiveElementBeforeDialog()
    focus.refocusAfterDialogCancel()
    expect(document.activeElement).toBe(document.body)
    el.remove()
  })
})
