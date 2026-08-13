import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import RevisionSnapshotView from './RevisionSnapshotView.vue'
import type { RevisionDetail } from '../../api/itemsApi'

const i18n = createI18n({
  legacy: false, locale: 'en', fallbackLocale: 'en',
  messages: { en: { revisions: {
    opUpdate: 'Updated', opUnknown: 'Changed', system: 'System', snapshot: 'Snapshot',
    revert: 'Revert to this revision', reverting: 'Reverting…',
    selectHint: 'Select a revision', loading: 'Loading…', colWhen: 'Time', colWho: 'By',
  } } },
})

const detail = { revisionNumber: 3, operation: 'update', createdAt: '2026-07-21T10:00:00Z', createdBy: 'user-1', snapshot: { status: 'draft' } }

type ViewProps = { detail: RevisionDetail | null; loading: boolean; error: string; canRevert: boolean; reverting?: boolean }

// No Button stub here: a name-keyed stub would blind this suite to which Button
// mounted (PrimeVue and the vendored component share the name). The `data-slot="button"`
// assertion below is what actually pins the vendored component; without a Button stub
// present, dropping that assertion would let a PrimeVue button pass every other check here
// undetected, since RotateCcw/the label live in a default slot both components render.
function mountView(props: ViewProps) {
  return mount(RevisionSnapshotView, { props, global: { plugins: [i18n] } })
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
    await w.get('.rev-revert-btn').trigger('click')
    expect(w.emitted('revert')).toEqual([[3]])
  })

  it('shows the error message when error is set', () => {
    const w = mountView({ detail: null, loading: false, error: 'boom', canRevert: true })
    expect(w.text()).toContain('boom')
  })

  it('shows a loading notice and hides the snapshot while loading', () => {
    const w = mountView({ detail, loading: true, error: '', canRevert: true })
    expect(w.text()).toContain('Loading…')
    expect(w.find('.rev-json').exists()).toBe(false)
  })

  it('falls back to the system label when createdBy is null', () => {
    const systemDetail = { ...detail, createdBy: null }
    const w = mountView({ detail: systemDetail, loading: false, error: '', canRevert: true })
    expect(w.text()).toContain('System')
    expect(w.text()).not.toContain('null')
  })

  it('renders a lucide revert glyph, not a primeicons class', () => {
    const w = mountView({ detail, loading: false, error: '', canRevert: true })
    expect(w.find('.lucide-rotate-ccw').exists()).toBe(true)
    expect(w.find('.pi').exists()).toBe(false)
  })

  it('types the revert button so it can never submit a surrounding form', () => {
    expect(mountView({ detail, loading: false, error: '', canRevert: true }).get('.rev-revert-btn').attributes('type')).toBe('button')
  })

  it('renders the vendored ui/button, not a PrimeVue one', () => {
    // PrimeVue's Button also renders a default slot and also passes through `type`, so
    // neither the icon/label content nor the `type` attribute distinguishes the two — only
    // this data-slot hook, which only the vendored Primitive-based button emits, does.
    expect(mountView({ detail, loading: false, error: '', canRevert: true }).get('.rev-revert-btn').attributes('data-slot')).toBe('button')
  })

  it('disables the revert button and swaps its label while reverting', () => {
    const reverting = mountView({ detail, loading: false, error: '', canRevert: true, reverting: true })
    expect(reverting.get('.rev-revert-btn').attributes('disabled')).toBeDefined()
    // ui/button has no loading prop, so the in-flight state is a label swap.
    expect(reverting.get('.rev-revert-btn').text()).toContain('Reverting…')

    const notReverting = mountView({ detail, loading: false, error: '', canRevert: true, reverting: false })
    expect(notReverting.get('.rev-revert-btn').attributes('disabled')).toBeUndefined()
    expect(notReverting.get('.rev-revert-btn').text()).toContain('Revert to this revision')
  })
})
