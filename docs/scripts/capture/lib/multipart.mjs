// Builds a multipart/form-data body with one file part, the way `curl -F "file=@name;type=..."`
// does. The boundary is fixed, so the same input always builds the same bytes.

export class MultipartError extends Error {}

const BOUNDARY = 'struo-capture-boundary-7d1f3a9c'
const UNSAFE = /["\r\n]/

function checkToken(label, value) {
  if (typeof value !== 'string' || value === '' || UNSAFE.test(value)) {
    throw new MultipartError(`${label} must be a non-empty text without a quote or a line break`)
  }
}

export function buildMultipart({ field, fileName, partType, content }) {
  checkToken('field', field)
  checkToken('file name', fileName)
  checkToken('part content type', partType)
  const closing = Buffer.from(`\r\n--${BOUNDARY}--\r\n`)
  if (content.includes(`--${BOUNDARY}`)) throw new MultipartError('the file content holds the multipart boundary')
  const head = Buffer.from(
    `--${BOUNDARY}\r\nContent-Disposition: form-data; name="${field}"; filename="${fileName}"\r\nContent-Type: ${partType}\r\n\r\n`,
  )
  return { body: Buffer.concat([head, content, closing]), contentType: `multipart/form-data; boundary=${BOUNDARY}` }
}
