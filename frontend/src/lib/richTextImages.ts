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

function rewriteImgSrc(html: string, srcFor: (id: string) => string): string {
  if (!html) return html
  const doc = new DOMParser().parseFromString(html, 'text/html')
  doc.querySelectorAll('img').forEach((img) => {
    const id = img.dataset.fileId
    if (id) img.setAttribute('src', srcFor(id))
  })
  return doc.body.innerHTML
}

/** For loading stored HTML into the editor: point each managed img at its display URL. */
export function absolutizeImageSrc(html: string): string {
  return rewriteImgSrc(html, fileContentDisplayUrl)
}

function escapeRegExp(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

// A managed image src is exactly the stored path or the display URL, optionally followed by a query.
const MANAGED_SRC = new RegExp(
  `^(?:${escapeRegExp(STORED_PREFIX)}|${escapeRegExp(apiBaseUrl)})/files/(${GUID})/content(?:\\?.*)?$`,
  'i',
)

function managedFileId(img: HTMLImageElement): string | null {
  const tagged = img.dataset.fileId
  if (tagged && GUID_PATTERN.test(tagged)) return tagged
  return MANAGED_SRC.exec(img.getAttribute('src') ?? '')?.[1] ?? null
}

/**
 * For emitting/storing: tag each managed img with its file id and point it at the base-independent
 * path in one parse. Images that are not managed are left as they are; html without an img is
 * returned unchanged.
 */
export function normalizeForStorage(html: string): string {
  if (!html || !/<img/i.test(html)) return html
  const doc = new DOMParser().parseFromString(html, 'text/html')
  doc.querySelectorAll('img').forEach((img) => {
    const id = managedFileId(img)
    if (!id) return
    img.dataset.fileId = id
    img.setAttribute('src', fileContentPath(id))
  })
  return doc.body.innerHTML
}
