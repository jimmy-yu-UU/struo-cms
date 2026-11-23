import assert from 'node:assert/strict'
import { readFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { test } from 'node:test'
import { fileURLToPath } from 'node:url'
import { inflateSync } from 'node:zlib'
import { SWATCH_HEIGHT, SWATCH_WIDTH, makeSwatchPng } from './swatch.mjs'

const HERE = dirname(fileURLToPath(import.meta.url))
const committed = (name) => readFileSync(join(HERE, name))

test('the committed swatch.png is exactly what the generator produces', () => {
  assert.deepEqual(committed('swatch.png'), makeSwatchPng())
})

test('the generator is deterministic and its output is a PNG of the declared size', () => {
  const png = makeSwatchPng()
  assert.deepEqual(png, makeSwatchPng())
  assert.equal(png.subarray(1, 4).toString('latin1'), 'PNG')
  assert.equal(png.readUInt32BE(16), SWATCH_WIDTH)
  assert.equal(png.readUInt32BE(20), SWATCH_HEIGHT)
  assert.equal([SWATCH_WIDTH, SWATCH_HEIGHT].join('x'), '64x48')
})

test('the image data inflates to one filter byte plus three bytes per pixel for every row', () => {
  const png = makeSwatchPng()
  const dataLength = png.readUInt32BE(33)
  assert.equal(png.subarray(37, 41).toString('latin1'), 'IDAT')
  const raw = inflateSync(png.subarray(41, 41 + dataLength))
  assert.equal(raw.length, SWATCH_HEIGHT * (1 + SWATCH_WIDTH * 3))
})

test('the text fixture is short plain text', () => {
  const text = committed('note.txt').toString('utf8')
  assert.ok(text.length > 0 && text.length < 100)
  assert.ok(!text.includes('\r'))
})
