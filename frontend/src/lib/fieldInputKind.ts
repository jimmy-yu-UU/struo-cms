export type InputKind =
  | 'text' | 'textarea' | 'richtext' | 'number' | 'boolean'
  | 'date' | 'time' | 'datetime' | 'select' | 'radio' | 'divider' | 'readonly'

const MAP: Record<string, InputKind> = {
  text: 'text', slug: 'text', email: 'text', url: 'text', color: 'text', phone: 'text', password: 'text',
  textarea: 'textarea', markdown: 'textarea', code: 'textarea',
  richText: 'richtext',
  number: 'number', slider: 'number', rating: 'number',
  boolean: 'boolean', checkbox: 'boolean',
  date: 'date', time: 'time', dateTime: 'datetime',
  select: 'select', radio: 'radio',
  divider: 'divider',
}

export function fieldInputKind(iface: string): InputKind {
  return MAP[iface] ?? 'readonly'
}
