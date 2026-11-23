// Chapter 15, files. The two fixtures are uploaded once each; the text file is switched to draft so
// an anonymous read of it is refused. The delete with an invalid purge value changes nothing.
import { STRIP_TRACE_ID, anonymous, exchange, read, setup } from './steps.mjs'

const FILES = '/api/files'
const NOTE_FILE = '/api/files/{{noteFileId}}'

export const ch15 = {
  name: 'chapter 15: files',
  file: '15-files-and-media.md',
  steps: [
    exchange('ch15-upload-swatch-201', 'POST', FILES, 201, {
      upload: { field: 'file', fixture: 'swatch.png', contentType: 'image/png' },
      save: { swatchFileId: 'data.id' },
      print: { headers: true, note: '(multipart: file=@swatch.png; type=image/png)' },
    }),
    setup('upload the text file', 'POST', FILES, 201, {
      upload: { field: 'file', fixture: 'note.txt', contentType: 'text/plain' },
      save: { noteFileId: 'data.id' },
    }),
    setup('switch the text file to draft', 'PUT', '/api/items/file/{{noteFileId}}', 200, { body: { status: 'draft' } }),
    anonymous('ch15-get-draft-anon-404', 'GET', NOTE_FILE, 404, { transforms: STRIP_TRACE_ID }),
    read('ch15-get-draft-admin', NOTE_FILE),
    anonymous('ch15-content-width5000-png', 'GET', '/api/files/{{swatchFileId}}/content?width=5000&format=png', 200, {
      print: { headers: ['Content-Length', 'Content-Type'], binary: 'png' },
    }),
    exchange('ch15-delete-purge-1-400', 'DELETE', '/api/files/{{noteFileId}}?purge=1', 400, { transforms: STRIP_TRACE_ID }),
  ],
}
