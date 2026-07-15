import type { ValidationDetail } from '../api/apiClient'

/**
 * Splits server-side validation `error.details` into per-field errors and
 * leftovers. A detail whose `field` matches a known field (case-insensitive —
 * ASP.NET ModelState keys do not preserve declared casing) is written to
 * `fieldErrors` under the meta's canonical name; everything else falls through
 * to `leftover` for the form's error banner.
 *
 * Only the first message per field is kept, matching `validateItem`'s
 * one-message-per-field convention.
 */
export function splitServerErrors(
  details: ValidationDetail[] | undefined,
  knownFields: ReadonlySet<string>,
): { fieldErrors: Record<string, string>; leftover: string[] } {
  const fieldErrors: Record<string, string> = {}
  const leftover: string[] = []
  if (!details || details.length === 0) return { fieldErrors, leftover }

  const canonicalByLower = new Map<string, string>()
  for (const name of knownFields) canonicalByLower.set(name.toLowerCase(), name)

  for (const d of details) {
    const canonical = canonicalByLower.get((d.field ?? '').toLowerCase())
    if (canonical !== undefined) {
      if (!(canonical in fieldErrors)) fieldErrors[canonical] = d.message
    } else {
      leftover.push(d.message)
    }
  }
  return { fieldErrors, leftover }
}
