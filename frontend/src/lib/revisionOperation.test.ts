import { describe, it, expect } from 'vitest'
import { revisionOperationKey } from './revisionOperation'

describe('revisionOperationKey', () => {
  it('maps known operations to their i18n keys', () => {
    expect(revisionOperationKey('create')).toBe('revisions.opCreate')
    expect(revisionOperationKey('update')).toBe('revisions.opUpdate')
    expect(revisionOperationKey('revert')).toBe('revisions.opRevert')
  })

  it('falls back to opUnknown for unrecognised operations', () => {
    expect(revisionOperationKey('something-else')).toBe('revisions.opUnknown')
    expect(revisionOperationKey('')).toBe('revisions.opUnknown')
  })
})
