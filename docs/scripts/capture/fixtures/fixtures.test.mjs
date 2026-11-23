import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { inflateSync } from 'node:zlib'
import { SWATCH_HEIGHT, SWATCH_WIDTH, makeSwatchPng, swatchPixels } from './swatch.mjs'

const HERE = dirname(fileURLToPath(import.meta.url))
const committed = (name) => readFileSync(join(HERE, name))
const SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
const IHDR_DATA_AT = 16
const COLOUR_TYPE_RGB = 2
const IDAT_LENGTH_AT = 33
const IDAT_DATA_AT = 41

// The committed file is checked by what it contains, not by the bytes one zlib version deflates to.
test('the committed swatch.png is a 64x48 RGB PNG whose pixels are the generator pixels', () => {
  const png = committed('swatch.png')
  assert.ok(png.subarray(0, 8).equals(SIGNATURE))
  assert.equal(png.subarray(12, 16).toString('latin1'), 'IHDR')
  assert.equal(png.readUInt32BE(IHDR_DATA_AT), SWATCH_WIDTH)
  assert.equal(png.readUInt32BE(IHDR_DATA_AT + 4), SWATCH_HEIGHT)
  assert.equal(png[IHDR_DATA_AT + 8], 8)
  assert.equal(png[IHDR_DATA_AT + 9], COLOUR_TYPE_RGB)
  assert.equal(png.subarray(IDAT_DATA_AT - 4, IDAT_DATA_AT).toString('latin1'), 'IDAT')
  const data = png.subarray(IDAT_DATA_AT, IDAT_DATA_AT + png.readUInt32BE(IDAT_LENGTH_AT))
  assert.ok(inflateSync(data).equals(swatchPixels()))
})

test('the generator makes a PNG of the declared size and the same pixels every time', () => {
  const png = makeSwatchPng()
  assert.equal(png.readUInt32BE(IHDR_DATA_AT), SWATCH_WIDTH)
  assert.equal(png.readUInt32BE(IHDR_DATA_AT + 4), SWATCH_HEIGHT)
  assert.equal([SWATCH_WIDTH, SWATCH_HEIGHT].join('x'), '64x48')
  assert.ok(swatchPixels().equals(swatchPixels()))
  assert.equal(swatchPixels().length, SWATCH_HEIGHT * (1 + SWATCH_WIDTH * 3))
})

test('the text fixture is short plain text', () => {
  const text = committed('note.txt').toString('utf8')
  assert.ok(text.length > 0 && text.length < 100)
  assert.ok(!text.includes('\r'))
})
