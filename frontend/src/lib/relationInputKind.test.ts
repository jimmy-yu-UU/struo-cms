import { describe, it, expect } from 'vitest'
import { relationInputKind } from './relationInputKind'

describe('relationInputKind', () => {
  it('maps the four supported interfaces', () => {
    expect(relationInputKind('dropdown')).toBe('dropdown')
    expect(relationInputKind('tagSelect')).toBe('tagSelect')
    expect(relationInputKind('treeSelect')).toBe('treeSelect')
    expect(relationInputKind('relatedList')).toBe('relatedList')
  })
  it('maps file relation interfaces and unknowns to readonly', () => {
    expect(relationInputKind('filePicker')).toBe('readonly')
    expect(relationInputKind('imagePicker')).toBe('readonly')
    expect(relationInputKind('filesPicker')).toBe('readonly')
    expect(relationInputKind('whatever')).toBe('readonly')
  })
})
