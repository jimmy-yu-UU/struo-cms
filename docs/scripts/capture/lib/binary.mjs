// The declared summary a block prints in place of a binary response body: the byte count and, for a
// declared kind, what the bytes decode to. The bytes themselves are never printed.

export class BinaryError extends Error {}

const PNG_SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])
const IHDR_END = 24

function pngLine(bytes) {
  const valid =
    bytes.length >= IHDR_END &&
    bytes.subarray(0, 8).equals(PNG_SIGNATURE) &&
    bytes.subarray(12, 16).toString('latin1') === 'IHDR'
  if (!valid) throw new BinaryError('the response body is not a PNG (no signature and IHDR chunk)')
  return `# decoded PNG IHDR: width=${bytes.readUInt32BE(16)}, height=${bytes.readUInt32BE(20)}`
}

// kind: true (byte count only) or the name of a kind the tool can decode.
export function summarizeBinary(bytes, kind) {
  const count = `# body: ${bytes.length} bytes (binary, not shown)`
  if (kind === true) return [count]
  if (kind === 'png') return [pngLine(bytes), count]
  throw new BinaryError(`unknown binary kind '${kind}'`)
}
