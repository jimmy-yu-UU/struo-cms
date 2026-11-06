export type LocaleValues = Record<string, unknown>
// One many-to-many link as the form edits it: the target id plus that link's junction payload values
// (keyed by the junction collection's camelCase field names). Only relations whose junction carries
// payload or a SortField use this shape — see lib/junctionLinks.ts usesLinksEditor().
export type RelationLink = { id: string; junction: Record<string, unknown> }
// Fields the server holds as null, which the form shows as ''. Not part of dirty tracking.
export type LoadedNulls = { shared: string[]; translations: Record<string, string[]> }
export type FormModel = {
  shared: Record<string, unknown>
  translations: Record<string, LocaleValues>
  relations: Record<string, unknown> // relation name -> id | id[] | RelationLink[] | null
  loadedNulls?: LoadedNulls // consulted on update so an untouched null field is not rewritten as ''
  version?: number // optimistic-concurrency token read from the item; echoed back on update
}
