import { describe, it, expect } from 'vitest'
import { fieldInputKind } from './fieldInputKind'

describe('fieldInputKind', () => {
  it('maps scalar text-family to text', () => {
    for (const i of ['text', 'slug', 'email', 'url', 'color', 'phone', 'password'])
      expect(fieldInputKind(i)).toBe('text')
  })
  it('maps textarea family and richText', () => {
    expect(fieldInputKind('textarea')).toBe('textarea')
    expect(fieldInputKind('markdown')).toBe('textarea')
    expect(fieldInputKind('code')).toBe('textarea')
    expect(fieldInputKind('richText')).toBe('richtext')
  })
  it('maps number, boolean, date families', () => {
    expect(fieldInputKind('number')).toBe('number')
    expect(fieldInputKind('boolean')).toBe('boolean')
    expect(fieldInputKind('checkbox')).toBe('boolean')
    expect(fieldInputKind('date')).toBe('date')
    expect(fieldInputKind('time')).toBe('time')
    expect(fieldInputKind('dateTime')).toBe('datetime')
  })
  it('maps select/radio and divider', () => {
    expect(fieldInputKind('select')).toBe('select')
    expect(fieldInputKind('radio')).toBe('radio')
    expect(fieldInputKind('divider')).toBe('divider')
  })
  it('falls back to readonly for deferred/unknown interfaces', () => {
    for (const i of ['file', 'image', 'files', 'multiSelect', 'checkboxGroup', 'tags', 'json', 'keyValue', 'repeater', 'uuid', 'somethingNew'])
      expect(fieldInputKind(i)).toBe('readonly')
  })
})
