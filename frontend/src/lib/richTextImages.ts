const API_BASE = import.meta.env.VITE_API_BASE_URL || '/api'

/** Stored, base-independent content path for a file id. */
export function fileContentPath(id: string): string {
  return `/api/files/${id}/content`
}

/** Absolute (or app-relative) URL for previewing a file in the editor. */
export function fileContentDisplayUrl(id: string): string {
  return `${API_BASE}/files/${id}/content`
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

/** For emitting/storing: point each managed img back at its base-independent path. */
export function relativizeImageSrc(html: string): string {
  return rewriteImgSrc(html, fileContentPath)
}
