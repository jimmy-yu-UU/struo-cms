import type { Component } from 'vue'
import type { FieldMeta } from '../../types/schema'
import type { FieldInterface, FieldTypeDef } from './types'
import TextField from '../../components/fields/TextField.vue'
import TextareaField from '../../components/fields/TextareaField.vue'
import RichTextField from '../../components/fields/RichTextField.vue'
import NumberField from '../../components/fields/NumberField.vue'
import BooleanField from '../../components/fields/BooleanField.vue'
import DateField from '../../components/fields/DateField.vue'
import SelectField from '../../components/fields/SelectField.vue'
import RadioField from '../../components/fields/RadioField.vue'
import DividerField from '../../components/fields/DividerField.vue'
import FileField from '../../components/fields/FileField.vue'
import ReadonlyField from '../../components/fields/ReadonlyField.vue'

type ListColumn = FieldTypeDef['listColumn']
const asString: ListColumn = { format: (v) => String(v) }
const asYesNo: ListColumn = { format: (v) => (v ? 'Yes' : 'No') }
const asDate: ListColumn = {
  format: (v) => { const d = new Date(String(v)); return Number.isNaN(d.getTime()) ? String(v) : d.toLocaleString() },
}
const asOption: ListColumn = {
  format: (v, f) => { const o = f.options?.find((x) => x.value === String(v)); return o ? o.label : String(v) },
}

function def(opts: {
  component: Component
  empty?: unknown
  serialize?: (v: unknown, f: FieldMeta) => unknown
  listColumn?: ListColumn
}): FieldTypeDef {
  const empty = 'empty' in opts ? opts.empty : ''
  return {
    component: opts.component,
    defaultValue: () => empty,
    parse: (raw) => raw ?? empty,
    serialize: opts.serialize ?? ((v) => v),
    listColumn: opts.listColumn ?? null,
  }
}

const fileSerialize = (v: unknown): unknown => (v === '' ? null : v)
const readonlyDef = def({ component: ReadonlyField })

export const registry: Record<FieldInterface, FieldTypeDef> = {
  text: def({ component: TextField, listColumn: asString }),
  slug: def({ component: TextField, listColumn: asString }),
  email: def({ component: TextField, listColumn: asString }),
  url: def({ component: TextField, listColumn: asString }),
  color: def({ component: TextField, listColumn: asString }),
  phone: def({ component: TextField, listColumn: asString }),
  password: def({ component: TextField }),
  textarea: def({ component: TextareaField, listColumn: asString }),
  markdown: def({ component: TextareaField }),
  code: def({ component: TextareaField }),
  richText: def({ component: RichTextField }),
  number: def({ component: NumberField, listColumn: asString }),
  slider: def({ component: NumberField, listColumn: asString }),
  rating: def({ component: NumberField, listColumn: asString }),
  boolean: def({ component: BooleanField, listColumn: asYesNo }),
  checkbox: def({ component: BooleanField, listColumn: asYesNo }),
  date: def({ component: DateField, listColumn: asDate }),
  time: def({ component: DateField, listColumn: asDate }),
  dateTime: def({ component: DateField, listColumn: asDate }),
  select: def({ component: SelectField, listColumn: asOption }),
  radio: def({ component: RadioField, listColumn: asOption }),
  divider: def({ component: DividerField }),
  file: def({ component: FileField, empty: null, serialize: fileSerialize }),
  image: def({ component: FileField, empty: null, serialize: fileSerialize }),
  // Deferred to later 7g+ slices — render read-only for now (unchanged behaviour).
  multiSelect: readonlyDef,
  checkboxGroup: readonlyDef,
  tags: readonlyDef,
  json: readonlyDef,
  keyValue: readonlyDef,
  repeater: readonlyDef,
  files: readonlyDef,
  hidden: readonlyDef,
  uuid: readonlyDef,
}

export function getFieldType(iface: string): FieldTypeDef {
  return registry[iface as FieldInterface] ?? readonlyDef
}
