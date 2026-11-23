// Print transforms: the only way a printed response body differs from the one the server sent.
// A step names each transform it uses; nothing else is ever applied. Every transform works on the
// raw text, never on a parsed and re-serialised copy, because re-serialising changes escapes
// (''' becomes "'") and the manual prints the server's own bytes.
//
// A transform that finds nothing to change throws: a declared transform that silently did nothing
// would print something other than what the step says it prints.

export class TransformError extends Error {}

function skipWhitespace(text, index) {
  let i = index
  while (i < text.length && ' \t\r\n'.includes(text[i])) i += 1
  return i
}

function skipString(text, start) {
  let i = start + 1
  while (i < text.length && text[i] !== '"') i += text[i] === '\\' ? 2 : 1
  if (i >= text.length) throw new TransformError('unterminated JSON string')
  return i + 1
}

// Index just past the JSON value that starts at `start`.
function skipValue(text, start) {
  const first = text[start]
  if (first === '"') return skipString(text, start)
  if (first === '{' || first === '[') {
    let depth = 0
    let i = start
    while (i < text.length) {
      const char = text[i]
      if (char === '"') {
        i = skipString(text, i)
        continue
      }
      if (char === '{' || char === '[') depth += 1
      if (char === '}' || char === ']') depth -= 1
      i += 1
      if (depth === 0) return i
    }
    throw new TransformError('unterminated JSON value')
  }
  let i = start
  while (i < text.length && !',}] \t\r\n'.includes(text[i])) i += 1
  return i
}

// The members of the object that starts at `start`: key, where the key token starts, and the
// span of the value.
function members(text, start) {
  let i = skipWhitespace(text, start)
  if (text[i] !== '{') throw new TransformError('not a JSON object')
  i = skipWhitespace(text, i + 1)
  const found = []
  while (text[i] !== '}') {
    if (text[i] !== '"') throw new TransformError('not a JSON object')
    const keyStart = i
    const keyEnd = skipString(text, i)
    i = skipWhitespace(text, keyEnd)
    if (text[i] !== ':') throw new TransformError('not a JSON object')
    const valueStart = skipWhitespace(text, i + 1)
    const valueEnd = skipValue(text, valueStart)
    found.push({ key: JSON.parse(text.slice(keyStart, keyEnd)), keyStart, valueStart, valueEnd })
    i = skipWhitespace(text, valueEnd)
    if (text[i] === ',') i = skipWhitespace(text, i + 1)
    else if (text[i] !== '}') throw new TransformError('not a JSON object')
  }
  return found
}

function topLevel(text) {
  try {
    return members(text, 0)
  } catch (error) {
    if (error instanceof TransformError) throw new TransformError('the body is not a JSON object')
    throw error
  }
}

// Removes the traceId member of the top-level `error` object, e.g. `,"traceId":"<32 hex>"`.
export function stripTraceId(text) {
  const error = topLevel(text).find((member) => member.key === 'error' && text[member.valueStart] === '{')
  if (!error) throw new TransformError('stripTraceId: the body has no error object')
  const inner = members(text, error.valueStart)
  const index = inner.findIndex((member) => member.key === 'traceId')
  if (index < 0) throw new TransformError('stripTraceId: the error object has no traceId')
  const trace = inner[index]
  if (!/^"[0-9a-f]{32}"$/.test(text.slice(trace.valueStart, trace.valueEnd))) {
    throw new TransformError('stripTraceId: the traceId is not a 32-digit hex string')
  }
  // Remove the comma that joins the member to its neighbour along with it.
  let from = trace.keyStart
  let to = trace.valueEnd
  if (index > 0) from = inner[index - 1].valueEnd
  else if (index + 1 < inner.length) to = inner[index + 1].keyStart
  return text.slice(0, from) + text.slice(to)
}

// Replaces the value of the top-level `data` array with the literal [...].
export function elideData(text) {
  const data = topLevel(text).find((member) => member.key === 'data')
  if (!data) throw new TransformError('elideData: the body has no top-level data member')
  if (text[data.valueStart] !== '[') throw new TransformError('elideData: data is not an array')
  return `${text.slice(0, data.valueStart)}[...]${text.slice(data.valueEnd)}`
}

export const TRANSFORMS = { stripTraceId, elideData }

export function applyTransforms(text, names) {
  return (names ?? []).reduce((current, name) => {
    const transform = TRANSFORMS[name]
    if (!transform) throw new TransformError(`unknown transform '${name}'`)
    return transform(current)
  }, text)
}
