import { nextTick } from 'vue'

export interface DialogFocus {
  focusIfStillInDocument: (el: HTMLElement) => () => void
  blurActiveElementBeforeDialog: (restore?: () => void) => void
  refocusAfterDialogCancel: () => void
  clearRestore: () => void
}

export function createDialogFocus(focusEditor: () => void): DialogFocus {
  // Focus handover for every dialog in this file. reka's modal Dialog applies aria-hidden to the
  // rest of the page on the same reactive flush that flips `open` true, so an element still holding
  // focus at that instant ends up inside the hidden subtree -- it must be blurred first.
  // Blurring to <body> also disables reka's own focus restore: it only captures a restore target
  // when activeElement is not <body>, so this pair is the only thing putting focus back on a cancel.
  // Paths that close by deliberately refocusing the editor (submit, remove, insert, select) clear
  // this themselves, so a restore never fights a focus move that was intended.
  let restoreFocusOnDialogCancel: (() => void) | null = null

  // Focusing a node that has since left the document is a silent no-op that leaves focus on <body>,
  // and several callers' captured elements (menu items, the bubble menu's link button) are unmounted
  // by the time a cancel runs -- so the editor is the fallback.
  function focusIfStillInDocument(el: HTMLElement): () => void {
    return () => {
      if (document.body.contains(el)) el.focus()
      else focusEditor()
    }
  }

  // `restore`, when given, replaces the default "focus whatever was focused before" capture: the alt
  // and table-size dialogs open from a menu/popover item that unmounts on the way in, so the element
  // holding focus right now is not a usable restore target for them.
  function blurActiveElementBeforeDialog(restore?: () => void): void {
    const active = document.activeElement
    restoreFocusOnDialogCancel = restore
      ?? (active instanceof HTMLElement && active !== document.body ? focusIfStillInDocument(active) : null)
    if (active instanceof HTMLElement) active.blur()
  }

  // The restore must be deferred a tick, not run synchronously. reka's FocusScope keeps its
  // document-level focus-trap listeners attached until Vue flushes the `open: false` prop change
  // that tears them down, and a focus() call made before that is caught by the still-active trap
  // and pulled straight back into the closing dialog.
  function refocusAfterDialogCancel(): void {
    const restore = restoreFocusOnDialogCancel
    restoreFocusOnDialogCancel = null
    if (!restore) return
    void nextTick().then(restore)
  }

  // `restoreFocusOnDialogCancel` is a module-scope closure variable, not reachable from
  // RichTextInput.vue -- callers that need to cancel a pending restore (submit, remove, insert,
  // select) go through this instead of assigning to it directly.
  function clearRestore(): void {
    restoreFocusOnDialogCancel = null
  }

  return { focusIfStillInDocument, blurActiveElementBeforeDialog, refocusAfterDialogCancel, clearRestore }
}
