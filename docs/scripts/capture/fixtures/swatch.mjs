// The PNG the upload scenario sends, generated deterministically: a 64x48 colour gradient, RGB,
// no interlacing. `node swatch.mjs` writes swatch.png next to this file; a test proves the
// committed bytes are what makeSwatchPng() returns.
import { writeFileSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { crc32, deflateSync } from 'node:zlib'

export const SWATCH_WIDTH = 64
export const SWATCH_HEIGHT = 48
const SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
const BIT_DEPTH = 8
const COLOUR_TYPE_RGB = 2

function chunk(type, data) {
  const head = Buffer.alloc(4)
  head.writeUInt32BE(data.length)
  const body = Buffer.concat([Buffer.from(type, 'latin1'), data])
  const crc = Buffer.alloc(4)
  crc.writeUInt32BE(crc32(body))
  return Buffer.concat([head, body, crc])
}

function pixels() {
  const rowLength = 1 + SWATCH_WIDTH * 3
  const raw = Buffer.alloc(SWATCH_HEIGHT * rowLength)
  for (let y = 0; y < SWATCH_HEIGHT; y += 1) {
    for (let x = 0; x < SWATCH_WIDTH; x += 1) {
      const at = y * rowLength + 1 + x * 3
      raw[at] = Math.round((x * 255) / (SWATCH_WIDTH - 1))
      raw[at + 1] = Math.round((y * 255) / (SWATCH_HEIGHT - 1))
      raw[at + 2] = 128
    }
  }
  return raw
}

export function makeSwatchPng() {
  const header = Buffer.alloc(13)
  header.writeUInt32BE(SWATCH_WIDTH, 0)
  header.writeUInt32BE(SWATCH_HEIGHT, 4)
  header[8] = BIT_DEPTH
  header[9] = COLOUR_TYPE_RGB
  return Buffer.concat([
    SIGNATURE,
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(pixels(), { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ])
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  writeFileSync(join(dirname(fileURLToPath(import.meta.url)), 'swatch.png'), makeSwatchPng())
}
