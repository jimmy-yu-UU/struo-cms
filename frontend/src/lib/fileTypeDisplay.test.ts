import { describe, it, expect } from 'vitest'
import { fileTypeDisplay } from './fileTypeDisplay'

describe('fileTypeDisplay', () => {
  it('maps Office/PDF content types to a dedicated-or-generic icon + short label', () => {
    expect(fileTypeDisplay('application/pdf')).toEqual({ icon: 'pi-file-pdf', label: 'PDF' })
    expect(fileTypeDisplay('application/vnd.openxmlformats-officedocument.presentationml.presentation'))
      .toEqual({ icon: 'pi-file', label: 'PPTX' })
    expect(fileTypeDisplay('application/vnd.openxmlformats-officedocument.wordprocessingml.document'))
      .toEqual({ icon: 'pi-file-word', label: 'DOCX' })
    expect(fileTypeDisplay('application/vnd.ms-excel')).toEqual({ icon: 'pi-file-excel', label: 'XLS' })
    expect(fileTypeDisplay('application/zip')).toEqual({ icon: 'pi-file', label: 'ZIP' })
    expect(fileTypeDisplay('application/x-zip-compressed')).toEqual({ icon: 'pi-file', label: 'ZIP' })
  })

  it('is case-insensitive on the content type', () => {
    expect(fileTypeDisplay('APPLICATION/PDF').label).toBe('PDF')
  })

  it('uses a media icon for image/video/audio families', () => {
    expect(fileTypeDisplay('video/mp4', 'clip.mp4')).toEqual({ icon: 'pi-video', label: 'MP4' })
    expect(fileTypeDisplay('audio/mpeg', 'song.mp3')).toEqual({ icon: 'pi-volume-up', label: 'MP3' })
    expect(fileTypeDisplay('image/png', 'a.png')).toEqual({ icon: 'pi-image', label: 'PNG' })
  })

  it('falls back to the filename extension for unknown types', () => {
    expect(fileTypeDisplay('application/octet-stream', 'archive.7z'))
      .toEqual({ icon: 'pi-file', label: '7Z' })
  })

  it('falls back to the MIME subtype, then FILE, when there is no usable extension', () => {
    expect(fileTypeDisplay('application/octet-stream')).toEqual({ icon: 'pi-file', label: 'OCTET-STREAM' })
    expect(fileTypeDisplay('', 'noext')).toEqual({ icon: 'pi-file', label: 'FILE' })
    expect(fileTypeDisplay(undefined)).toEqual({ icon: 'pi-file', label: 'FILE' })
  })
})
