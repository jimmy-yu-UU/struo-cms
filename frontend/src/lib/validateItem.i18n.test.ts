import { describe, it, expect } from 'vitest'
import { createI18n } from 'vue-i18n'
import en from '@/locales/en'
import zhTW from '@/locales/zh-TW'
import { validateItem } from './validateItem'
import type { CollectionMeta, FieldMeta, RelationMeta } from '../types/schema'
import type { FormModel } from '../types/itemForm'

const i18n = createI18n({ legacy: false, locale: 'zh-TW', fallbackLocale: 'en', messages: { en, 'zh-TW': zhTW } })
const t = (key: string, params?: Record<string, unknown>): string => i18n.global.t(key, params ?? {}) as string
const CJK = /[一-鿿]/
const LATIN_SENTENCE = /\b(is required|must be at most)\b/

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: false, sort: 0, isSystem: false, ...over }
}

describe('validateItem localized messages', () => {
  const meta: CollectionMeta = { name: 'article', label: 'Article', relations: [], fields: [
    field('status', { label: 'Status', required: true }),
    field('name', { label: 'Name', maxLength: 5 }),
    field('title', { label: 'Title', translatable: true, required: true }),
  ] }
  const model: FormModel = {
    shared: { status: '', name: 'x'.repeat(6) },
    translations: { en: { title: '' } },
    relations: {},
  }

  it('builds the required and max-length sentences from zh-TW keys', () => {
    const errs = validateItem(meta, model, 'en', undefined, undefined, t)
    expect(errs.status).toBe(t('validation.required', { label: 'Status' }))
    expect(errs.name).toBe(t('validation.maxLength', { label: 'Name', max: 5 }))
    expect(errs.title).toBe(t('validation.required', { label: 'Title' }))
    for (const msg of Object.values(errs)) {
      expect(msg).toMatch(CJK)
      expect(msg).not.toMatch(LATIN_SENTENCE)
    }
  })

  it('localizes relation row messages with the 1-based row number', () => {
    const rel: RelationMeta = { name: 'tags', label: 'Tags', kind: 'manyToMany', targetCollection: 'tag',
      interface: 'tagSelect', foreignKey: null, displayTemplate: '{Name}', editable: true, selfReferencing: false,
      junctionCollection: 'articleTag', junctionPayloadFields: ['note'], sortField: null }
    const junction: CollectionMeta = { name: 'articleTag', label: 'Article tag', relations: [], fields: [
      field('articleId', { interface: 'uuid' }), field('tagId', { interface: 'uuid' }),
      field('note', { label: 'Note', required: true, sort: 1 }),
    ] }
    const m: CollectionMeta = { ...meta, fields: [], relations: [rel] }
    const links = [{ id: 'a', junction: { note: 'ok' } }, { id: 'b', junction: { note: '' } }]
    const errs = validateItem(m, { shared: {}, translations: {}, relations: { tags: links } },
      'en', () => junction, undefined, t)
    expect(errs.tags).toBe(t('validation.rowRequired', { relation: 'Tags', label: 'Note', row: 2 }))
    expect(errs.tags).toMatch(CJK)
    expect(errs.tags).not.toMatch(LATIN_SENTENCE)
  })

  it('keeps the English wording by default', () => {
    expect(validateItem(meta, model, 'en').status).toBe('Status is required.')
  })
})
