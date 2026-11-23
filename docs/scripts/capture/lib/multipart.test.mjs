import assert from 'node:assert/strict'
import { test } from 'node:test'
import { MultipartError, buildMultipart } from './multipart.mjs'

const CRLF = '\r\n'

test('one file part carries the field name, the file name and the declared part content type', () => {
  const { body, contentType } = buildMultipart({
    field: 'file',
    fileName: 'a.txt',
    partType: 'text/plain',
    content: Buffer.from('hello'),
  })
  const boundary = /^multipart\/form-data; boundary=(.+)$/.exec(contentType)[1]
  const expected = [
    `--${boundary}`,
    'Content-Disposition: form-data; name="file"; filename="a.txt"',
    'Content-Type: text/plain',
    '',
    'hello',
    `--${boundary}--`,
    '',
  ].join(CRLF)
  assert.equal(body.toString('latin1'), expected)
})

test('the bytes of the file go in unchanged, binary included', () => {
  const content = Buffer.from([0x89, 0x50, 0x00, 0xff, 0x0d, 0x0a, 0x1a])
  const { body } = buildMultipart({ field: 'f', fileName: 'x.bin', partType: 'application/octet-stream', content })
  assert.ok(body.includes(content))
})

test('the same input builds the same bytes', () => {
  const input = { field: 'file', fileName: 'a.txt', partType: 'text/plain', content: Buffer.from('x') }
  assert.deepEqual(buildMultipart(input), buildMultipart(input))
})

test('a content that holds the boundary is refused', () => {
  const first = buildMultipart({ field: 'f', fileName: 'a', partType: 't/p', content: Buffer.from('x') })
  const boundary = /boundary=(.+)$/.exec(first.contentType)[1]
  assert.throws(
    () => buildMultipart({ field: 'f', fileName: 'a', partType: 't/p', content: Buffer.from(`--${boundary}`) }),
    MultipartError,
  )
})

test('a field, file name or part type with a quote or a line break is refused', () => {
  const good = { field: 'f', fileName: 'a', partType: 't/p', content: Buffer.from('x') }
  for (const bad of [{ field: 'a"b' }, { fileName: 'a\nb' }, { partType: 'a\rb' }, { fileName: '' }]) {
    assert.throws(() => buildMultipart({ ...good, ...bad }), MultipartError)
  }
})
