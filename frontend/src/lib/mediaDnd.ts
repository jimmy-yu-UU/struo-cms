import { DRAG_MIME, parseMovePayload, serializeMovePayload, type MovePayload } from './mediaMove'

/** Writes the payload onto a dragstart event. */
export function setDragPayload(ev: DragEvent, payload: MovePayload): void {
  ev.dataTransfer?.setData(DRAG_MIME, serializeMovePayload(payload))
  if (ev.dataTransfer) ev.dataTransfer.effectAllowed = 'move'
}

/** True when the drag carries a media payload — used to accept or ignore dragover. */
export function isMediaDrag(ev: DragEvent): boolean {
  return Array.from(ev.dataTransfer?.types ?? []).includes(DRAG_MIME)
}

/** Reads and validates the payload from a drop event; null when the drop is not ours. */
export function readDragPayload(ev: DragEvent): MovePayload | null {
  const raw = ev.dataTransfer?.getData(DRAG_MIME)
  return raw ? parseMovePayload(raw) : null
}
