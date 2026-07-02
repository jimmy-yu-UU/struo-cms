import type { FieldMeta } from '../types/schema'

export function formatCell(value: unknown, field: FieldMeta): string {
  if (value === null || value === undefined || value === '') return '—'

  const iface = field.interface
  if ((iface === 'select' || iface === 'radio') && field.options) {
    const opt = field.options.find((o) => o.value === String(value))
    return opt ? opt.label : String(value)
  }
  if (iface === 'boolean' || iface === 'checkbox') {
    return value ? 'Yes' : 'No'
  }
  if (iface === 'date' || iface === 'time' || iface === 'dateTime') {
    const d = new Date(String(value))
    return Number.isNaN(d.getTime()) ? String(value) : d.toLocaleString()
  }
  return String(value)
}
