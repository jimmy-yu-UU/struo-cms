import { describe, it, expect, vi, beforeEach } from 'vitest'

const sonner = vi.hoisted(() => ({
  success: vi.fn(), info: vi.fn(), warning: vi.fn(), error: vi.fn(),
}))
vi.mock('vue-sonner', () => ({ toast: sonner }))

import { useToast } from './useToast'

describe('useToast', () => {
  beforeEach(() => { vi.clearAllMocks() })

  it('routes each severity to its sonner counterpart', () => {
    const toast = useToast()
    toast.add({ severity: 'success', summary: 'Saved' })
    toast.add({ severity: 'info', summary: 'Note' })
    toast.add({ severity: 'warn', summary: 'Careful' })
    toast.add({ severity: 'error', summary: 'Failed' })
    expect(sonner.success).toHaveBeenCalledWith('Saved', expect.any(Object))
    expect(sonner.info).toHaveBeenCalledWith('Note', expect.any(Object))
    expect(sonner.warning).toHaveBeenCalledWith('Careful', expect.any(Object))
    expect(sonner.error).toHaveBeenCalledWith('Failed', expect.any(Object))
  })

  it('defaults to info when severity is omitted', () => {
    useToast().add({ summary: 'Plain' })
    expect(sonner.info).toHaveBeenCalledWith('Plain', expect.any(Object))
  })

  it('passes detail through as the description and life as the duration', () => {
    useToast().add({ severity: 'success', summary: 'Saved', detail: '3 items', life: 5000 })
    expect(sonner.success).toHaveBeenCalledWith('Saved', { description: '3 items', duration: 5000 })
  })

  it('uses the summary-less detail as the title so a message is never swallowed', () => {
    useToast().add({ severity: 'error', detail: 'Network down' })
    expect(sonner.error).toHaveBeenCalledWith('Network down', { description: undefined, duration: undefined })
  })
})
