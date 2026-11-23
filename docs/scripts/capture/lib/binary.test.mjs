import assert from 'node:assert/strict'
import { test } from 'node:test'
import { BinaryError, summarizeBinary } from './binary.mjs'

const PNG_SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]

function png(width, height, extra = 10) {
  const header = Buffer.alloc(16 + 8 + extra)
  Buffer.from(PNG_SIGNATURE).copy(header)
  header.writeUInt32BE(13, 8)
  header.write('IHDR', 12, 'latin1')
  header.writeUInt32BE(width, 16)
  header.writeUInt32BE(height, 20)
  return header
}

test('a declared png prints the decoded size and the byte count, and never the bytes', () => {
  const lines = summarizeBinary(png(64, 48, 100), 'png')
  assert.deepEqual(lines, ['# decoded PNG IHDR: width=64, height=48', '# body: 124 bytes (binary, not shown)'])
})

test('bytes that are not a PNG fail the declared png summary', () => {
  assert.throws(() => summarizeBinary(Buffer.from('not a png at all, really not one'), 'png'), BinaryError)
  assert.throws(() => summarizeBinary(Buffer.alloc(5), 'png'), BinaryError)
})

test('a declared kind the tool does not know is refused', () => {
  assert.throws(() => summarizeBinary(png(1, 1), 'gif'), /unknown binary kind 'gif'/)
})

test('a kind-less summary prints the byte count only', () => {
  assert.deepEqual(summarizeBinary(Buffer.from('abc'), true), ['# body: 3 bytes (binary, not shown)'])
})
