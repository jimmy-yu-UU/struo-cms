// Maps a file's content type (with the filename as fallback) to an icon token (the "pi-" prefix
// dialect resolveIcon in lib/icons.ts accepts) and a short, human-readable label (e.g. "PDF",
// "PPTX") for non-previewable files. Replaces showing the raw MIME string, which is long and ugly
// for Office/OOXML types. PDF/Word/Excel/plain-text get a dedicated icon below; other types fall
// back to the generic file or media icon, and the short label carries the format identity.

export type FileTypeDisplay = { icon: string; label: string }

// Exact content-type → { dedicated-or-generic icon, short label }.
const BY_CONTENT_TYPE: Record<string, FileTypeDisplay> = {
  'application/pdf': { icon: 'pi-file-pdf', label: 'PDF' },
  'application/msword': { icon: 'pi-file-word', label: 'DOC' },
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document': { icon: 'pi-file-word', label: 'DOCX' },
  'application/vnd.ms-excel': { icon: 'pi-file-excel', label: 'XLS' },
  'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet': { icon: 'pi-file-excel', label: 'XLSX' },
  'application/vnd.ms-powerpoint': { icon: 'pi-file', label: 'PPT' },
  'application/vnd.openxmlformats-officedocument.presentationml.presentation': { icon: 'pi-file', label: 'PPTX' },
  'application/zip': { icon: 'pi-file', label: 'ZIP' },
  'application/x-zip-compressed': { icon: 'pi-file', label: 'ZIP' },
  'text/plain': { icon: 'pi-file-edit', label: 'TXT' },
}

function extensionLabel(fileName: string | undefined): string {
  const dot = fileName ? fileName.lastIndexOf('.') : -1
  if (dot < 0 || dot === (fileName?.length ?? 0) - 1) return ''
  return fileName!.slice(dot + 1).toUpperCase()
}

export function fileTypeDisplay(contentType: string | undefined, fileName?: string): FileTypeDisplay {
  const ct = (contentType ?? '').trim().toLowerCase()

  const exact = BY_CONTENT_TYPE[ct]
  if (exact) return exact

  const ext = extensionLabel(fileName)
  if (ct.startsWith('image/')) return { icon: 'pi-image', label: ext || 'IMAGE' }
  if (ct.startsWith('video/')) return { icon: 'pi-video', label: ext || 'VIDEO' }
  if (ct.startsWith('audio/')) return { icon: 'pi-volume-up', label: ext || 'AUDIO' }

  // Unknown type: the filename extension is the most meaningful label; fall back to the MIME
  // subtype (e.g. "octet-stream") only when there is no extension, and to "FILE" as a last resort.
  const subtype = ct.includes('/') ? ct.split('/')[1].toUpperCase() : ''
  return { icon: 'pi-file', label: ext || subtype || 'FILE' }
}
