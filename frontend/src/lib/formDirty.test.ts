import { describe, it, expect } from 'vitest'
import { snapshotModel, isDirty, unsavedConfirm } from './formDirty'
import type { FormModel } from '../types/itemForm'

function sample(): FormModel {
  return {
    shared: { title: 'Hello', status: 'draft' },
    translations: { en: { body: 'Hi' }, fr: { body: 'Salut' } },
    relations: { tags: ['a', 'b'], category: 'c1' },
    version: 3,
  }
}

describe('snapshotModel / isDirty', () => {
  it('is not dirty when nothing changed', () => {
    const m = sample()
    const base = snapshotModel(m)
    expect(isDirty(base, m)).toBe(false)
  })

  it('is dirty when a shared field changes', () => {
    const m = sample()
    const base = snapshotModel(m)
    m.shared.status = 'published'
    expect(isDirty(base, m)).toBe(true)
  })

  it('is dirty when a deep translation value changes', () => {
    const m = sample()
    const base = snapshotModel(m)
    ;(m.translations.fr as Record<string, unknown>).body = 'Bonjour'
    expect(isDirty(base, m)).toBe(true)
  })

  it('is dirty when a relations array changes', () => {
    const m = sample()
    const base = snapshotModel(m)
    ;(m.relations.tags as string[]).push('c')
    expect(isDirty(base, m)).toBe(true)
  })

  it('ignores version changes so 409-conflict recovery does not falsely mark dirty', () => {
    const m = sample()
    const base = snapshotModel(m)
    m.version = 99
    expect(isDirty(base, m)).toBe(false)
  })
})

describe('unsavedConfirm', () => {
  it('resolves the header and message via the injected translator', () => {
    const t = (key: string): string => key
    const c = unsavedConfirm(t)
    expect(c.header).toBe('confirm.unsavedHeader')
    expect(c.message).toBe('confirm.unsavedMessage')
  })
})
