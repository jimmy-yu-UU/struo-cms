import { describe, it, expect, vi } from 'vitest'
import { revisionOperationKey, revisionOperationLabel } from './revisionOperation'

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

describe('revisionOperationLabel', () => {
  // Fake translator: renders the key alongside its params so assertions can pin exactly which
  // key/params were requested without needing a real vue-i18n instance.
  const t = vi.fn((key: string, params?: Record<string, unknown>) =>
    params ? `${key}:${JSON.stringify(params)}` : key,
  )

  it('annotates a revert that recorded which revision it restored', () => {
    expect(revisionOperationLabel('revert', 1, t)).toBe('revisions.opRevertFrom:{"n":1}')
  })

  it('falls back to the plain operation label for a revert with no recorded source', () => {
    expect(revisionOperationLabel('revert', null, t)).toBe('revisions.opRevert')
    expect(revisionOperationLabel('revert', undefined, t)).toBe('revisions.opRevert')
  })

  it('never annotates non-revert operations even if a source number is present', () => {
    expect(revisionOperationLabel('update', 1, t)).toBe('revisions.opUpdate')
    expect(revisionOperationLabel('create', 1, t)).toBe('revisions.opCreate')
  })

  it('falls back to the unknown label for an unrecognised operation', () => {
    expect(revisionOperationLabel('something-else', null, t)).toBe('revisions.opUnknown')
  })
})
