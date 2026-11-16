import type { FieldInterface } from './types'

/**
 * Whether a field's column holds text, decided from its interface alone (the metadata response carries
 * nothing else about the column type). A string operator such as `_contains` is accepted on a text column
 * and answered with a 400 on any other. The backend's text columns are string properties and JSON columns;
 * this lists the interfaces that store one of those. An enumerated interface (select, radio) is left out:
 * its value is a pick from the options and its property can be an enum.
 */
const TEXT_INTERFACE: Record<FieldInterface, boolean> = {
  text: true, textarea: true, richText: true, markdown: true, code: true,
  slug: true, email: true, url: true, password: false, color: true, phone: true,
  number: false, slider: false, rating: false,
  boolean: false, checkbox: false,
  date: false, time: false, dateTime: false,
  select: false, radio: false,
  multiSelect: true, checkboxGroup: true, tags: true, keyValue: true, repeater: true, files: true,
  json: true,
  file: false, image: false,
  hidden: false, divider: false, uuid: false,
}

export function isTextInterface(fieldInterface: string): boolean {
  return Object.hasOwn(TEXT_INTERFACE, fieldInterface) && TEXT_INTERFACE[fieldInterface as FieldInterface]
}
