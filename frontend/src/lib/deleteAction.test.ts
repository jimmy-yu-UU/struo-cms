import { describe, it, expect } from 'vitest'
import { deleteKindFor, deleteConfirm, purgeConfirm } from './deleteAction'

describe('deleteKindFor', () => {
  it('returns soft when the collection opts into soft delete', () => {
    expect(deleteKindFor({ softDelete: true })).toBe('soft')
  })
  it('returns hard when softDelete is false, missing, or meta is nullish', () => {
    expect(deleteKindFor({ softDelete: false })).toBe('hard')
    expect(deleteKindFor({})).toBe('hard')
    expect(deleteKindFor(null)).toBe('hard')
    expect(deleteKindFor(undefined)).toBe('hard')
  })
})

describe('confirm copy', () => {
  it('soft delete confirm mentions restoring', () => {
    expect(deleteConfirm('soft')).toEqual({
      header: 'Move to trash',
      message: 'Move this item to trash? You can restore it later.',
    })
  })
  it('hard delete confirm matches the existing irreversible copy', () => {
    expect(deleteConfirm('hard')).toEqual({
      header: 'Confirm delete',
      message: 'Delete this item? This cannot be undone.',
    })
  })
  it('purge confirm is strongly worded', () => {
    expect(purgeConfirm()).toEqual({
      header: 'Delete permanently',
      message: 'Permanently delete this item? This cannot be undone.',
    })
  })
})
