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
]}

describe('validateItem', () => {
  it('flags empty required shared and default-locale required translatable', () => {
    const model: FormModel = { shared: { status: '' }, translations: { en: { title: '' }, 'zh-TW': { title: '' } } }
    const errs = validateItem(meta, model, 'en')
    expect(errs.status).toMatch(/required/i)
    expect(errs.title).toMatch(/required/i)
  })
  it('does not block on missing non-default locale', () => {
    const model: FormModel = { shared: { status: 'draft' }, translations: { en: { title: 'Hi' }, 'zh-TW': { title: '' } } }
    expect(validateItem(meta, model, 'en')).toEqual({})
  })
})
