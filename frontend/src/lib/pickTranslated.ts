export type TranslationMap = Record<string, Record<string, unknown>> | undefined

/** Default locale first; else the first locale carrying a non-empty value. Keeps list
 *  columns and relation-picker labels readable when content exists only in another locale. */
export function pickTranslated(translations: TranslationMap, defaultCode: string, field: string): unknown {
  const primary = translations?.[defaultCode]?.[field]
  if (primary != null && primary !== '') return primary
  for (const code of Object.keys(translations ?? {})) {
    const v = translations?.[code]?.[field]
    if (v != null && v !== '') return v
  }
  return undefined
}
