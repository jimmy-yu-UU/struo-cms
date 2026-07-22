import { describe, it, expect } from 'vitest'
import { deleteKindFor, deleteConfirm, purgeConfirm } from './deleteAction'

// Identity stub translator: asserts the helper resolves to the right i18n *key*
// (FE-16) without needing a real vue-i18n instance.
const t = (key: string): string => key

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
  it('soft delete confirm resolves the trash/restore keys', () => {
    expect(deleteConfirm(t, 'soft')).toEqual({
      header: 'confirm.softDeleteHeader',
      message: 'confirm.softDeleteMessage',
    })
  })
  it('hard delete confirm resolves the irreversible-delete keys', () => {
    expect(deleteConfirm(t, 'hard')).toEqual({
      header: 'confirm.hardDeleteHeader',
      message: 'confirm.hardDeleteMessage',
    })
  })
  it('purge confirm resolves the strongly-worded permanent-delete keys', () => {
    expect(purgeConfirm(t)).toEqual({
      header: 'confirm.purgeHeader',
      message: 'confirm.purgeMessage',
    })
  })

  it('invokes the translator with each expected key (not just a hardcoded string)', () => {
    const seen: string[] = []
    const spy = (key: string): string => { seen.push(key); return key }
    deleteConfirm(spy, 'soft')
    deleteConfirm(spy, 'hard')
    purgeConfirm(spy)
    expect(seen).toEqual([
      'confirm.softDeleteHeader', 'confirm.softDeleteMessage',
      'confirm.hardDeleteHeader', 'confirm.hardDeleteMessage',
      'confirm.purgeHeader', 'confirm.purgeMessage',
    ])
  })
})
