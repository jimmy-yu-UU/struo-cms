import { describe, it, expect } from 'vitest'
import { ALL_FIELD_INTERFACES } from './types'
import { isTextInterface } from './textInterfaces'

describe('isTextInterface', () => {
  it('is true for the interfaces stored as a string or as JSON text', () => {
    for (const name of ['text', 'textarea', 'richText', 'markdown', 'code', 'slug', 'email', 'url', 'color',
      'phone', 'json', 'multiSelect', 'checkboxGroup', 'tags', 'keyValue', 'repeater', 'files']) {
      expect(isTextInterface(name), name).toBe(true)
    }
  })

  it('is false for numbers, booleans, dates, picks from options, files and ids', () => {
    for (const name of ['number', 'slider', 'rating', 'boolean', 'checkbox', 'date', 'time', 'dateTime',
      'select', 'radio', 'file', 'image', 'uuid', 'hidden', 'divider', 'password']) {
      expect(isTextInterface(name), name).toBe(false)
    }
  })

  it('is false for a name it does not know, and answers for every interface the SPA declares', () => {
    expect(isTextInterface('somethingNew')).toBe(false)
    expect(isTextInterface('toString')).toBe(false)
    for (const name of ALL_FIELD_INTERFACES) expect(typeof isTextInterface(name)).toBe('boolean')
  })
})
