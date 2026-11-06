import type { CollectionMeta, LanguageInfo } from '../types/schema'
import type { LoadedNulls } from '../types/itemForm'
import { splitFields } from './splitFields'

// Text-like fields parse null to '' and serialize '' back unchanged, so the form alone cannot tell
// "never set" from "cleared". These helpers track the fields the server holds as null.

export function loadedNullsOf(
  meta: CollectionMeta,
  item: Record<string, unknown>,
  locales: LanguageInfo[],
): LoadedNulls {
  const { shared, translatable } = splitFields(meta)
  const itemTranslations = (item.translations ?? {}) as Record<string, Record<string, unknown> | null>
  const translations: Record<string, string[]> = {}
  for (const loc of locales) {
    const src = itemTranslations[loc.code] ?? {}
    const names = translatable.filter((f) => src[f.name] === null).map((f) => f.name)
    if (names.length > 0) translations[loc.code] = names
  }
  return { shared: shared.filter((f) => item[f.name] === null).map((f) => f.name), translations }
}

// The null fields after `payload` was saved: a field sent as null is null, one sent with any other
// value is not; fields and locales the payload leaves out keep their previous state.
export function nullsAfterSave(
  meta: CollectionMeta,
  previous: LoadedNulls | undefined,
  payload: Record<string, unknown>,
  locales: LanguageInfo[],
): LoadedNulls {
  const { shared, translatable } = splitFields(meta)
  const sentTranslations = (payload.translations ?? {}) as Record<string, Record<string, unknown> | null>
  const keptShared = new Set(previous?.shared)
  for (const f of shared) {
    if (!(f.name in payload)) continue
    if (payload[f.name] === null) keptShared.add(f.name)
    else keptShared.delete(f.name)
  }
  const translations: Record<string, string[]> = {}
  for (const loc of locales) {
    const sent = sentTranslations[loc.code]
    const before = new Set(previous?.translations[loc.code])
    const names = loc.code in sentTranslations
      ? translatable.filter((f) => sent === null || sent?.[f.name] === null).map((f) => f.name)
      : translatable.filter((f) => before.has(f.name)).map((f) => f.name)
    if (names.length > 0) translations[loc.code] = names
  }
  return { shared: shared.filter((f) => keptShared.has(f.name)).map((f) => f.name), translations }
}
