export type LocaleValues = Record<string, unknown>
export type FormModel = {
  shared: Record<string, unknown>
  translations: Record<string, LocaleValues>
  relations: Record<string, unknown> // relation name -> id | id[] | null
  version?: number // optimistic-concurrency token read from the item; echoed back on update (D2)
}
