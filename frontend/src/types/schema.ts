// Mirrors backend DTOs. JSON is camelCase; enum values are camelCase strings
// (e.g. FieldInterface.RichText -> "richText", DateTime -> "dateTime").

export type FieldOption = { value: string; label: string }

// A single free-form tag: stored value + optional manual display label (label ?? value shown).
export type TagItem = { value: string; label?: string }

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
  maxLength?: number | null // effective CMS-layer limit resolved by the backend scanner; null = unlimited
  helpText?: string | null
  group?: string | null
  options?: FieldOption[] | null
  fields?: FieldMeta[] | null // Repeater sub-field metadata (Phase 7g+ slice 4)
  isSystem: boolean
}

export type RelationMeta = {
  name: string
  label: string
  kind: string // camelCase RelationKind, e.g. "manyToOne" | "manyToMany" | "oneToMany"
  targetCollection: string
  interface: string // camelCase RelationInterface: "dropdown" | "tagSelect" | "treeSelect" | "relatedList"
  foreignKey?: string | null // CLR property name on the owning/child entity, e.g. "CategoryId"
  displayTemplate?: string | null // e.g. "{Name}"
  editable: boolean
  selfReferencing: boolean
}

export type CollectionMeta = {
  name: string
  label: string
  icon?: string | null
  group?: string | null
  defaultDisplayField?: string | null
  softDelete?: boolean // Phase 9b: true when the collection's entity implements ISoftDeletable
  fields: FieldMeta[]
  relations: RelationMeta[]
}

export type CollectionPermission = { read: boolean; write: boolean; delete: boolean }

export type CurrentUserDto = {
  id: string
  isSuperAdmin: boolean
  permissions: Record<string, CollectionPermission>
}

export type LanguageInfo = { code: string; name: string; isDefault: boolean }
