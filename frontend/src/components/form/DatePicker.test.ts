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

function iso(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
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
    expect(w.get('button').text()).toMatch(/\b11\b/)
  })

  it('gives the trigger an accessible name that combines the field label with the current value, and updates as the value changes', async () => {
    const w = mount(DatePicker, { props: { modelValue: null, label: 'Published at' }, ...opts })
    const before = `Published at: ${en.fields.pickADate}`
    expect(w.get('button').attributes('aria-label')).toBe(before)

    await w.setProps({ modelValue: new Date(2026, 7, 11) })
    const after = w.get('button').attributes('aria-label')
    // aria-label overrides element contents rather than supplementing them, so both the field
    // label and the current value must be present in the one attribute, and it must not be a
    // fixed string that hides which value is stored.
    expect(after).toMatch(/^Published at: /)
    expect(after).toMatch(/2026/)
    expect(after).not.toBe(before)
  })

  it('falls back to the value or placeholder alone as the accessible name when no field label is given', () => {
    const w = mount(DatePicker, { props: { modelValue: null, placeholder: 'Custom placeholder' }, ...opts })
    expect(w.get('button').attributes('aria-label')).toBe('Custom placeholder')
  })

  it('disables the trigger', () => {
    const w = mount(DatePicker, { props: { modelValue: null, disabled: true }, ...opts })
    expect(w.get('button').attributes('disabled')).toBeDefined()
  })

  // Native <button> defaults to type="submit". This trigger is dispatched inside ItemForm.vue's
  // <form @submit.prevent>, so an untyped button here would submit the whole record on a click
  // that should only open the calendar popover.
  it('gives the trigger an explicit type="button"', () => {
    const w = mount(DatePicker, { props: { modelValue: null }, ...opts })
    expect(w.get('button').attributes('type')).toBe('button')
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
  it('preserves the full time-of-day (down to milliseconds) of the incoming value when only the date changes', async () => {
    const withTime = new Date(2026, 7, 11, 14, 30, 15, 250)
    const w = mount(DatePicker, { props: { modelValue: withTime }, ...opts })
    await open(w)
    await w.get('[data-value="2026-08-12"]').trigger('click')
    const emitted = w.emitted('update:modelValue')?.[0]?.[0] as Date
    expect(emitted.getDate()).toBe(12)
    expect(emitted.getHours()).toBe(14)
    expect(emitted.getMinutes()).toBe(30)
    expect(emitted.getSeconds()).toBe(15)
    // Milliseconds alone, asserted separately: a regression that carries hours/minutes/seconds
    // but drops milliseconds would pass every assertion above.
    expect(emitted.getMilliseconds()).toBe(250)
  })

  it('emits null when the already-selected day is clicked again', async () => {
    const w = mount(DatePicker, { props: { modelValue: new Date(2026, 7, 11) }, ...opts })
    await open(w)
    // reka's calendar deselects (and emits undefined, mapped here to null) when the currently
    // selected day is clicked again, since preventDeselect defaults to false.
    await w.get('[data-value="2026-08-11"]').trigger('click')
    expect(w.emitted('update:modelValue')?.[0]).toEqual([null])
  })

  it('defaults to midnight when picking a day with no incoming value (the fresh-create, passive-mode path)', async () => {
    const w = mount(DatePicker, { props: { modelValue: null }, ...opts })
    await open(w)
    // No modelValue means no fixed target day to click, so target today's cell — the calendar
    // with no default-placeholder input falls back to opening on the current month regardless.
    await w.get('[data-today]').trigger('click')
    const emitted = w.emitted('update:modelValue')?.[0]?.[0] as Date
    const now = new Date()
    expect(emitted.getFullYear()).toBe(now.getFullYear())
    expect(emitted.getMonth()).toBe(now.getMonth())
    expect(emitted.getDate()).toBe(now.getDate())
    expect(emitted.getHours()).toBe(0)
    expect(emitted.getMinutes()).toBe(0)
    expect(emitted.getSeconds()).toBe(0)
    expect(emitted.getMilliseconds()).toBe(0)
  })

  it('reflects a model value set after mount, including which day the calendar shows selected', async () => {
    const w = mount(DatePicker, { props: { modelValue: null }, ...opts })
    expect(w.get('button').text()).toContain(en.fields.pickADate)
    await w.setProps({ modelValue: new Date(2026, 7, 20) })
    expect(w.get('button').text()).toMatch(/2026/)
    expect(w.get('button').text()).toMatch(/\b20\b/)
    await open(w)
    expect(w.get('[data-value="2026-08-20"]').attributes('data-selected')).toBeDefined()
  })

  // Regression test for the calendar opening on the current month instead of the model value's
  // month: computed from the real clock (not a fixed date) so it cannot coincidentally pass
  // whatever month the suite happens to run in, and it fails without default-placeholder wired
  // to calendarValue on <Calendar> because Calendar falls back to opening on today.
  it('opens on the model value\'s month no matter how far it is from the current month', async () => {
    const now = new Date()
    // Day 15 sidesteps month-length edge cases when adding 6 months.
    const farAway = new Date(now.getFullYear(), now.getMonth() + 6, 15)
    const w = mount(DatePicker, { props: { modelValue: farAway }, ...opts })
    await open(w)
    const cell = w.find(`[data-value="${iso(farAway)}"]`)
    expect(cell.exists()).toBe(true)
    expect(cell.attributes('data-selected')).toBeDefined()
  })
})
