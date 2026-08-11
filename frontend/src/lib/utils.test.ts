import { describe, it, expect } from 'vitest'
import { cn } from '@/lib/utils'

describe('cn', () => {
  it('joins truthy class names', () => {
    expect(cn('a', 'b')).toBe('a b')
  })

  it('drops falsy values', () => {
    expect(cn('a', false, undefined, null, 'b')).toBe('a b')
  })

  // The whole reason tailwind-merge exists: a later utility must beat an earlier
  // one in the SAME group rather than both surviving and letting source order decide.
  it('lets a later Tailwind utility win within the same group', () => {
    expect(cn('p-2', 'p-4')).toBe('p-4')
    expect(cn('text-sm', 'text-lg')).toBe('text-lg')
  })

  it('keeps utilities from different groups', () => {
    expect(cn('p-2', 'text-sm')).toBe('p-2 text-sm')
  })
})
