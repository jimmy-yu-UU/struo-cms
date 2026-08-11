import { describe, it, expect } from 'vitest'
import { mount, type VueWrapper } from '@vue/test-utils'
import { createI18n } from 'vue-i18n'
import DatePicker from './DatePicker.vue'
import en from '@/locales/en'

const i18n = createI18n({ legacy: false, locale: 'en', messages: { en } })
// reka's own portal wrapper is itself named Teleport, so it collides with VTU's teleport stub and
// drops slot content unless renderStubDefaultSlot is on.
const opts = { global: { plugins: [i18n], stubs: { teleport: true }, renderStubDefaultSlot: true } }

// PopoverContent is gated by reka's Presence, so the calendar's day cells do not exist in the DOM
// until the trigger has actually been clicked open — mirrors what a real user does.
async function open(w: VueWrapper): Promise<void> {
  await w.get('button').trigger('click')
  await w.vm.$nextTick()
  await w.vm.$nextTick()
}

describe('DatePicker', () => {
  it('shows the placeholder when there is no value', () => {
    const w = mount(DatePicker, { props: { modelValue: null }, ...opts })
    expect(w.get('button').text()).toContain(en.fields.pickADate)
  })

  it('formats the current value on the trigger', () => {
    const w = mount(DatePicker, { props: { modelValue: new Date(2026, 7, 11) }, ...opts })
    // Asserting the year+day is enough to prove formatting ran without pinning a locale's month
    // spelling, which would make the test fail the moment the app locale changes.
    expect(w.get('button').text()).toMatch(/2026/)
    expect(w.get('button').text()).toMatch(/11/)
  })

  it('gives the trigger an accessible name that does not depend on the displayed value', () => {
    const empty = mount(DatePicker, { props: { modelValue: null }, ...opts })
    const withValue = mount(DatePicker, { props: { modelValue: new Date(2026, 7, 11) }, ...opts })
    expect(empty.get('button').attributes('aria-label')).toBe(en.fields.pickADate)
    // Same accessible name whether or not a value is showing — a screen reader announcing only
    // "August 11, 2026, button" would give no clue what the control does.
    expect(withValue.get('button').attributes('aria-label')).toBe(en.fields.pickADate)
  })

  it('disables the trigger', () => {
    const w = mount(DatePicker, { props: { modelValue: null, disabled: true }, ...opts })
    expect(w.get('button').attributes('disabled')).toBeDefined()
  })

  it('emits the picked date through a real click on the calendar day cell', async () => {
    const w = mount(DatePicker, { props: { modelValue: new Date(2026, 7, 1) }, ...opts })
    await open(w)
    await w.get('[data-value="2026-08-15"]').trigger('click')
    const emitted = w.emitted('update:modelValue')?.[0]?.[0] as Date
    expect(emitted.getFullYear()).toBe(2026)
    expect(emitted.getMonth()).toBe(7)
    expect(emitted.getDate()).toBe(15)
  })

  // The whole reason this component converts by hand: ui/calendar speaks CalendarDate, which has
  // no time part. Picking a new day on a value that carries a time must keep that time, or every
  // dateTime field silently resets to midnight the first time someone touches the date.
  it('preserves the time part of the incoming value when only the date changes', async () => {
    const withTime = new Date(2026, 7, 11, 14, 30, 15)
    const w = mount(DatePicker, { props: { modelValue: withTime }, ...opts })
    await open(w)
    await w.get('[data-value="2026-08-12"]').trigger('click')
    const emitted = w.emitted('update:modelValue')?.[0]?.[0] as Date
    expect(emitted.getDate()).toBe(12)
    expect(emitted.getHours()).toBe(14)
    expect(emitted.getMinutes()).toBe(30)
    expect(emitted.getSeconds()).toBe(15)
  })

  it('emits null when the already-selected day is clicked again', async () => {
    const w = mount(DatePicker, { props: { modelValue: new Date(2026, 7, 11) }, ...opts })
    await open(w)
    // reka's calendar deselects (and emits undefined, mapped here to null) when the currently
    // selected day is clicked again, since preventDeselect defaults to false.
    await w.get('[data-value="2026-08-11"]').trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([null])
  })

  it('reflects a model value set after mount, including which day the calendar shows selected', async () => {
    const w = mount(DatePicker, { props: { modelValue: null }, ...opts })
    expect(w.get('button').text()).toContain(en.fields.pickADate)
    await w.setProps({ modelValue: new Date(2026, 7, 20) })
    expect(w.get('button').text()).toMatch(/2026/)
    expect(w.get('button').text()).toMatch(/20/)
    await open(w)
    expect(w.get('[data-value="2026-08-20"]').attributes('data-selected')).toBeDefined()
  })
})
