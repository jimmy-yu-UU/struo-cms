import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RevisionSnapshotView from './RevisionSnapshotView.vue'
import type { RevisionDetail } from '../../api/itemsApi'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { revisions: {
    opUpdate: 'Updated', opUnknown: 'Changed', system: 'System', snapshot: 'Snapshot',
    revert: 'Revert to this revision', selectHint: 'Select a revision', loading: 'Loading…',
  } } },
})
const stubs = { Button: true }
const detail = { revisionNumber: 3, operation: 'update', createdAt: '2026-07-21T10:00:00Z', createdBy: 'user-1', snapshot: { status: 'draft' } }

type ViewProps = { detail: RevisionDetail | null; loading: boolean; error: string; canRevert: boolean }

function mountView(props: ViewProps) {
  return mount(RevisionSnapshotView, { props, global: { plugins: [i18n], stubs } })
}

describe('RevisionSnapshotView', () => {
  it('renders the summary and pretty-printed JSON snapshot', () => {
    const w = mountView({ detail, loading: false, error: '', canRevert: true })
    expect(w.text()).toContain('Updated')
    expect(w.find('.rev-json').text()).toContain('"status": "draft"')
  })

  it('shows a hint when no detail is selected', () => {
    const w = mountView({ detail: null, loading: false, error: '', canRevert: true })
    expect(w.text()).toContain('Select a revision')
    expect(w.find('.rev-json').exists()).toBe(false)
  })

  it('renders the revert button only when canRevert', () => {
    expect(mountView({ detail, loading: false, error: '', canRevert: true }).find('.rev-revert-btn').exists()).toBe(true)
    expect(mountView({ detail, loading: false, error: '', canRevert: false }).find('.rev-revert-btn').exists()).toBe(false)
  })

  it('emits revert with the revision number when the button is clicked', async () => {
    const w = mountView({ detail, loading: false, error: '', canRevert: true })
    await w.find('.rev-revert-btn').trigger('click')
    expect(w.emitted('revert')?.[0]).toEqual([3])
  })

  it('shows the error message when error is set', () => {
    const w = mountView({ detail: null, loading: false, error: 'boom', canRevert: true })
    expect(w.text()).toContain('boom')
  })
})
