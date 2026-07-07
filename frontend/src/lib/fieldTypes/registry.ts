import type { Component } from 'vue'
import type { FieldMeta, TagItem } from '../../types/schema'
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
import MultiSelectField from '../../components/fields/MultiSelectField.vue'
import CheckboxGroupField from '../../components/fields/CheckboxGroupField.vue'
import TagsField from '../../components/fields/TagsField.vue'

type ListColumn = FieldTypeDef['listColumn']
const asString: ListColumn = { format: (v) => String(v) }
const asYesNo: ListColumn = { format: (v) => (v ? 'Yes' : 'No') }
const asDate: ListColumn = {
  format: (v) => { const d = new Date(String(v)); return Number.isNaN(d.getTime()) ? String(v) : d.toLocaleString() },
}
const asOption: ListColumn = {
  format: (v, f) => { const o = f.options?.find((x) => x.value === String(v)); return o ? o.label : String(v) },
}
const asJoinedOptions: ListColumn = {
  format: (v, f) => (Array.isArray(v)
    ? v.map((x) => f.options?.find((o) => o.value === String(x))?.label ?? String(x)).join(', ')
    : String(v ?? '')),
}
const asJoinedTags: ListColumn = {
  format: (v) => (Array.isArray(v)
    ? (v as TagItem[]).map((t) => t?.label ?? t?.value ?? '').join(', ')
    : String(v ?? '')),
}

const arrParse = (raw: unknown): unknown[] => (Array.isArray(raw) ? raw : [])

function optionMultiDef(component: Component): FieldTypeDef {
  return {
    component,
    defaultValue: () => [],
    parse: arrParse,
    serialize: (v) => {
      if (!Array.isArray(v)) return []
      const seen = new Set<string>()
      const out: string[] = []
      for (const x of v) {
        const s = String(x)
        if (s.trim() === '' || seen.has(s)) continue
        seen.add(s); out.push(s)
      }
      return out
    },
    listColumn: asJoinedOptions,
  }
}

const tagsDef: FieldTypeDef = {
  component: TagsField,
  defaultValue: () => [],
  parse: arrParse,
  serialize: (v) => {
    if (!Array.isArray(v)) return []
    const seen = new Set<string>()
    const out: TagItem[] = []
    for (const t of v as Array<Partial<TagItem>>) {
      const value = (t?.value ?? '').trim()
      if (value === '' || seen.has(value)) continue
      seen.add(value)
      const label = (t?.label ?? '').trim()
      out.push(label ? { value, label } : { value })
    }
    return out
  },
  listColumn: asJoinedTags,
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
  multiSelect: optionMultiDef(MultiSelectField),
  checkboxGroup: optionMultiDef(CheckboxGroupField),
  tags: tagsDef,
  // Deferred to later 7g+ slices — render read-only for now (unchanged behaviour).
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
