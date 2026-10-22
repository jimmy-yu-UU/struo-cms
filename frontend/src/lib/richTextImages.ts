import { apiBaseUrl } from '../api/apiClient'

const GUID = '[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
const GUID_PATTERN = new RegExp(`^${GUID}$`, 'i')
const STORED_PREFIX = '/api'

/** Stored, base-independent content path for a file id. */
export function fileContentPath(id: string): string {
  return `${STORED_PREFIX}/files/${id}/content`
}

/** Absolute (or app-relative) URL for previewing a file in the editor. */
export function fileContentDisplayUrl(id: string): string {
  return `${apiBaseUrl}/files/${id}/content`
}

function escapeRegExp(text: string): string {
  return text.replaceAll(/[.*+?^${}()|[\]\\]/g, String.raw`\$&`)
}

const QUERY = String.raw`(\?.*)?`

// A managed image src is the stored path or the display URL, optionally followed by a query.
const MANAGED_SRC = new RegExp(
  `^(?:${escapeRegExp(STORED_PREFIX)}|${escapeRegExp(apiBaseUrl)})/files/(${GUID})/content${QUERY}$`,
  'i',
)

type ManagedImage = { id: string, query: string }

// An img is managed when its src is a managed URL, or when it has no src and a valid data-file-id.
// A data-file-id on any other src is ignored, so foreign images are never rewritten.
function managedImage(img: HTMLImageElement): ManagedImage | null {
  const src = img.getAttribute('src') ?? ''
  const match = MANAGED_SRC.exec(src)
  if (match) return { id: match[1], query: match[2] ?? '' }
  const tagged = img.dataset.fileId
  if (!src && tagged && GUID_PATTERN.test(tagged)) return { id: tagged, query: '' }
  return null
}

function rewriteManagedImages(html: string, srcFor: (image: ManagedImage) => string, tag: boolean): string {
  if (!html || !/<img/i.test(html)) return html
  const doc = new DOMParser().parseFromString(html, 'text/html')
  doc.querySelectorAll('img').forEach((img) => {
    const image = managedImage(img)
    if (!image) return
    if (tag) img.dataset.fileId = image.id
    img.setAttribute('src', srcFor(image))
  })
  return doc.body.innerHTML
}

/** For loading stored HTML into the editor: point each managed img at its display URL. */
export function absolutizeImageSrc(html: string): string {
  return rewriteManagedImages(html, ({ id, query }) => fileContentDisplayUrl(id) + query, false)
}

/**
 * For emitting/storing: tag each managed img with its file id and point it at the base-independent
 * path in one parse. A query string such as image transform parameters is kept. Images that are not
 * managed are left as they are; html without an img is returned unchanged.
 */
export function normalizeForStorage(html: string): string {
  return rewriteManagedImages(html, ({ id, query }) => fileContentPath(id) + query, true)
}
