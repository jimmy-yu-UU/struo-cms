import { describe, it, expect } from 'vitest'
import { blankItemForm } from './parseItemToForm'
import { validateItem } from './validateItem'
import { buildItemPayload } from './buildItemPayload'
import { localeIsSent } from './translationErrors'
import { hasLocaleContent } from './localeCompleteness'
import { splitFields } from './splitFields'
import { snapshotModel, baselineTranslations } from './formDirty'
import type { CollectionMeta, FieldMeta, LanguageInfo } from '../types/schema'

function field(name: string, over: Partial<FieldMeta> = {}): FieldMeta {
  return { name, label: name, interface: 'text', required: false, searchable: false, sortable: false,
    readOnly: false, hidden: false, translatable: true, sort: 0, isSystem: false, ...over }
}
const meta: CollectionMeta = { name: 'article', label: 'Article', relations: [], fields: [
  field('id', { isSystem: true, translatable: false }),
  field('title', { required: true }),
  field('tagList', { interface: 'tags' }),
  field('gallery', { interface: 'files' }),
  field('picks', { interface: 'multiSelect' }),
  field('rows', { interface: 'repeater', fields: [field('name', { translatable: false })] }),
  field('attrs', { interface: 'keyValue' }),
  field('note', { interface: 'text' }),
  field('body', { interface: 'richText' }),
]}
const locales: LanguageInfo[] = [
  { code: 'en', name: 'English', isDefault: true },
  { code: 'zh-TW', name: 'zh', isDefault: false },
]
const { translatable } = splitFields(meta)

describe('blank form with only the default locale title filled', () => {
  const model = blankItemForm(meta, locales)
  model.translations.en.title = 'Hello'
  // A cleared rich text field emits null.
  model.translations['zh-TW'].body = null

  it('raises no zh-TW validation errors', () => {
    const errors = validateItem(meta, model, 'en')
    expect(Object.keys(errors).filter((k) => k.startsWith('zh-TW:'))).toEqual([])
  })
  it('sends no zh-TW translation on create', () => {
    const p = buildItemPayload(meta, model, locales, 'create')
    expect(Object.keys(p.translations as object)).toEqual(['en'])
  })
  it('agrees between hasLocaleContent and localeIsSent', () => {
    for (const code of ['en', 'zh-TW']) {
      expect(hasLocaleContent(translatable, model.translations[code]))
        .toBe(localeIsSent(translatable, model.translations[code]))
    }
    expect(hasLocaleContent(translatable, model.translations['zh-TW'])).toBe(false)
  })
  it.each(['tagList', 'gallery', 'picks', 'rows', 'attrs', 'note', 'body'])('%s alone does not make zh-TW sent', (name) => {
    expect(localeIsSent(translatable, { [name]: model.translations['zh-TW'][name] })).toBe(false)
  })
})

describe('update: clearing a non-default locale', () => {
  const loaded = { en: { title: 'Hi' }, 'zh-TW': { title: 'zh', tagList: [] } }
  const baseline = (m: Record<string, Record<string, unknown>>) =>
    baselineTranslations(snapshotModel({ shared: {}, translations: m, relations: {} }))

  it('sends null for a non-default locale that had content and is now empty', () => {
    const model = blankItemForm(meta, locales)
    model.translations.en.title = 'Hi'
    const p = buildItemPayload(meta, model, locales, 'update', { baselineTranslations: baseline(loaded) })
    expect(p.translations).toEqual({ en: expect.objectContaining({ title: 'Hi' }), 'zh-TW': null })
  })
  it('does not send null for a locale that was already empty', () => {
    const model = blankItemForm(meta, locales)
    model.translations.en.title = 'Hi'
    const p = buildItemPayload(meta, model, locales, 'update',
      { baselineTranslations: baseline({ en: { title: 'Hi' }, 'zh-TW': { title: '', tagList: [] } }) })
    expect(Object.keys(p.translations as object)).toEqual(['en'])
  })
  it('sends the emptied default locale as per-field nulls, never as null itself', () => {
    const model = blankItemForm(meta, locales)
    model.translations['zh-TW'].title = 'zh'
    const p = buildItemPayload(meta, model, locales, 'update', { baselineTranslations: baseline(loaded) })
    const t = p.translations as Record<string, unknown>
    expect(Object.keys(t)).toEqual(['en', 'zh-TW'])
    expect(t.en).not.toBeNull()
    expect(Object.values(t.en as object).every((v) => v === null)).toBe(true)
  })
  it('sends the new content, not null, when the locale still has content', () => {
    const model = blankItemForm(meta, locales)
    model.translations['zh-TW'].title = 'new'
    const p = buildItemPayload(meta, model, locales, 'update', { baselineTranslations: baseline(loaded) })
    expect((p.translations as Record<string, unknown>)['zh-TW']).toEqual(expect.objectContaining({ title: 'new' }))
  })
  it('sends no null on create', () => {
    const model = blankItemForm(meta, locales)
    model.translations.en.title = 'Hi'
    const p = buildItemPayload(meta, model, locales, 'create', { baselineTranslations: baseline(loaded) })
    expect(Object.keys(p.translations as object)).toEqual(['en'])
  })
  it('sends no null without a baseline', () => {
    const model = blankItemForm(meta, locales)
    model.translations.en.title = 'Hi'
    const p = buildItemPayload(meta, model, locales, 'update')
    expect(Object.keys(p.translations as object)).toEqual(['en'])
  })
})
