// Mirrors backend DTOs. JSON is camelCase; enum values are camelCase strings
// (e.g. FieldInterface.RichText -> "richText", DateTime -> "dateTime").

export type FieldOption = { value: string; label: string }

export type FieldMeta = {
  name: string
  label: string
  interface: string // camelCase FieldInterface, e.g. "text" | "select" | "dateTime" | "richText"
  required: boolean
  searchable: boolean
  sortable: boolean
  readOnly: boolean
  hidden: boolean
  translatable: boolean
  sort: number
  helpText?: string | null
  group?: string | null
  options?: FieldOption[] | null
  isSystem: boolean
}

export type CollectionMeta = {
  name: string
  label: string
  icon?: string | null
  group?: string | null
  defaultDisplayField?: string | null
  fields: FieldMeta[]
}

export type CollectionPermission = { read: boolean; write: boolean; delete: boolean }

export type CurrentUserDto = {
  id: string
  isSuperAdmin: boolean
  permissions: Record<string, CollectionPermission>
}
