import { describe, it, expect } from 'vitest'
import { validateItem } from './validateItem'
import type { CollectionMeta, FieldMeta } from '../types/schema'
import type { FormModel } from '../types/itemForm'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', fields: [
  field('status', { required: true }),
  field('title', { translatable: true, required: true }),
], relations: [] }

describe('validateItem', () => {
  it('flags empty required shared and default-locale required translatable', () => {
    const model: FormModel = { shared: { status: '' }, translations: { en: { title: '' }, 'zh-TW': { title: '' } }, relations: {} }
    const errs = validateItem(meta, model, 'en')
    expect(errs.status).toMatch(/required/i)
    expect(errs.title).toMatch(/required/i)
  })
  it('does not block on missing non-default locale', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } }, relations: {} }
    expect(validateItem(meta, model, 'en')).toEqual({})
  })
  it('rejects over-long shared values and accepts exactly-at-limit', () => {
    const lenMeta: CollectionMeta = { name: 'article', label: 'Article', fields: [
      field('name', { label: 'Name', maxLength: 5 }),
    ], relations: [] }
    const tooLongModel: FormModel = { shared: { name: 'x'.repeat(6) }, translations: {}, relations: {} }
    expect(validateItem(lenMeta, tooLongModel, 'en')).toEqual({ name: 'Name must be at most 5 characters.' })
    const atLimitModel: FormModel = { shared: { name: 'x'.repeat(5) }, translations: {}, relations: {} }
    expect(validateItem(lenMeta, atLimitModel, 'en')).toEqual({})
  })
  it('rejects over-long default-locale translatable values', () => {
    const lenMeta: CollectionMeta = { name: 'article', label: 'Article', fields: [
      field('title', { label: 'Title', translatable: true, maxLength: 5 }),
    ], relations: [] }
    const model: FormModel = { shared: {}, translations: { en: { title: 'x'.repeat(6) } }, relations: {} }
    expect(validateItem(lenMeta, model, 'en')).toEqual({ title: 'Title must be at most 5 characters.' })
  })
})
