// Print transforms: the only way a printed response body differs from the one the server sent.
// A step names each transform it uses; nothing else is ever applied. Every transform works on the
// raw text, never on a parsed and re-serialised copy, because re-serialising changes escapes
// (''' becomes "'") and the manual prints the server's own bytes.
//
// A transform that finds nothing to change throws: a declared transform that silently did nothing
// would print something other than what the step says it prints.

export class TransformError extends Error {}

const WHITESPACE = ' \t\r\n'
const LITERAL_END = `,}]${WHITESPACE}`
const NOT_OBJECT = 'not a JSON object'

function skipWhitespace(text, index) {
  let i = index
  while (i < text.length && WHITESPACE.includes(text[i])) i += 1
  return i
}

function skipString(text, start) {
  let i = start + 1
  while (i < text.length && text[i] !== '"') i += text[i] === '\\' ? 2 : 1
  if (i >= text.length) throw new TransformError('unterminated JSON string')
  return i + 1
}

// Index just past the object or array that starts at `start`, strings and nesting included.
function skipComposite(text, start) {
  let depth = 0
  let i = start
  while (i < text.length) {
    if (text[i] === '"') {
      i = skipString(text, i)
    } else {
      depth += bracketDepthChange(text[i])
      i += 1
      if (depth === 0) return i
    }
  }
  throw new TransformError('unterminated JSON value')
}

function bracketDepthChange(char) {
  if (char === '{' || char === '[') return 1
  if (char === '}' || char === ']') return -1
  return 0
}

// Index just past the number, true, false or null that starts at `start`.
function skipLiteral(text, start) {
  let i = start
  while (i < text.length && !LITERAL_END.includes(text[i])) i += 1
  return i
}

// Index just past the JSON value that starts at `start`.
function skipValue(text, start) {
  const first = text[start]
  if (first === '"') return skipString(text, start)
  if (first === '{' || first === '[') return skipComposite(text, start)
  return skipLiteral(text, start)
}

// The members of the object that starts at `start`: key, where the key token starts, and the
// span of the value.
function members(text, start) {
  let i = skipWhitespace(text, start)
  if (text[i] !== '{') throw new TransformError(NOT_OBJECT)
  i = skipWhitespace(text, i + 1)
  const found = []
  while (text[i] !== '}') {
    if (text[i] !== '"') throw new TransformError(NOT_OBJECT)
    const keyStart = i
    const keyEnd = skipString(text, i)
    i = skipWhitespace(text, keyEnd)
    if (text[i] !== ':') throw new TransformError(NOT_OBJECT)
    const valueStart = skipWhitespace(text, i + 1)
    const valueEnd = skipValue(text, valueStart)
    found.push({ key: JSON.parse(text.slice(keyStart, keyEnd)), keyStart, valueStart, valueEnd })
    i = skipWhitespace(text, valueEnd)
    if (text[i] === ',') i = skipWhitespace(text, i + 1)
    else if (text[i] !== '}') throw new TransformError(NOT_OBJECT)
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

const TRACE_VALUE = /^"[0-9a-f]{32}"$/

// The span to cut from `text` to remove the traceId member of the object at `objectStart`, together
// with the comma that joins it to a neighbour; null when the object has no traceId.
function traceSpan(text, objectStart, name) {
  const inner = members(text, objectStart)
  const index = inner.findIndex((member) => member.key === 'traceId')
  if (index < 0) return null
  const trace = inner[index]
  if (!TRACE_VALUE.test(text.slice(trace.valueStart, trace.valueEnd))) {
    throw new TransformError(`${name}: the traceId is not a 32-digit hex string`)
  }
  let from = trace.keyStart
  let to = trace.valueEnd
  if (index > 0) from = inner[index - 1].valueEnd
  else if (index + 1 < inner.length) to = inner[index + 1].keyStart
  return { from, to }
}

// Removes the traceId member of the top-level `error` object, e.g. `,"traceId":"<32 hex>"`.
export function stripTraceId(text) {
  const error = topLevel(text).find((member) => member.key === 'error' && text[member.valueStart] === '{')
  if (!error) throw new TransformError('stripTraceId: the body has no error object')
  const span = traceSpan(text, error.valueStart, 'stripTraceId')
  if (!span) throw new TransformError('stripTraceId: the error object has no traceId')
  return text.slice(0, span.from) + text.slice(span.to)
}

// The start of each element of the array that starts at `start`.
function elementStarts(text, start) {
  const starts = []
  let i = skipWhitespace(text, start + 1)
  while (text[i] !== ']') {
    starts.push(i)
    i = skipWhitespace(text, skipValue(text, i))
    if (text[i] === ',') i = skipWhitespace(text, i + 1)
    else if (text[i] !== ']') throw new TransformError('not a JSON array')
  }
  return starts
}

// Removes the traceId member of the `extensions` object of every entry of the top-level `errors`
// array, the form a GraphQL response carries it in.
export function stripGraphQlTraceId(text) {
  const errors = topLevel(text).find((member) => member.key === 'errors' && text[member.valueStart] === '[')
  if (!errors) throw new TransformError('stripGraphQlTraceId: the body has no errors array')
  const spans = elementStarts(text, errors.valueStart)
    .filter((start) => text[start] === '{')
    .flatMap((start) => members(text, start).filter((member) => member.key === 'extensions' && text[member.valueStart] === '{'))
    .map((extensions) => traceSpan(text, extensions.valueStart, 'stripGraphQlTraceId'))
    .filter((span) => span !== null)
  if (spans.length === 0) throw new TransformError('stripGraphQlTraceId: no error has a traceId in its extensions')
  return [...spans].reverse().reduce((current, span) => current.slice(0, span.from) + current.slice(span.to), text)
}

// Replaces the value of the top-level `data` array with the literal [...].
export function elideData(text) {
  const data = topLevel(text).find((member) => member.key === 'data')
  if (!data) throw new TransformError('elideData: the body has no top-level data member')
  if (text[data.valueStart] !== '[') throw new TransformError('elideData: data is not an array')
  return `${text.slice(0, data.valueStart)}[...]${text.slice(data.valueEnd)}`
}

const PRETTY_ERROR = 'prettyJson: the body is not a single JSON value'
const PRETTY_NOTHING = 'prettyJson: the body is a scalar or an empty object or array, which has nothing to indent'
const PRETTY_INDENTED = 'prettyJson: the body is already indented as it would be, so the transform changes nothing'
const INDENT = '  '

const notPretty = () => new TransformError(PRETTY_ERROR)

// The key token of an object member that starts at `start`, with `: ` after it, and the index of its value.
function readKey(text, start) {
  if (text[start] !== '"') throw notPretty()
  const keyEnd = skipString(text, start)
  const colon = skipWhitespace(text, keyEnd)
  if (text[colon] !== ':') throw notPretty()
  return { label: `${text.slice(start, keyEnd)}: `, valueAt: skipWhitespace(text, colon + 1) }
}

// The index after the comma that follows a member, or the member's end when the closer follows.
function afterMember(text, end, close) {
  const i = skipWhitespace(text, end)
  if (text[i] === ',') return skipWhitespace(text, i + 1)
  if (text[i] !== close) throw notPretty()
  return i
}

// The indented lines of the members of the object or array whose first member starts at `start`.
function indentMembers(text, start, { isObject, close, depth }) {
  const pad = INDENT.repeat(depth + 1)
  const parts = []
  let i = start
  while (i < text.length && text[i] !== close) {
    const key = isObject ? readKey(text, i) : { label: '', valueAt: i }
    const member = indent(text, key.valueAt, depth + 1)
    parts.push(pad + key.label + member.out)
    i = afterMember(text, member.end, close)
  }
  if (text[i] !== close) throw notPretty()
  return { parts, end: i + 1 }
}

// The text of the JSON value that starts at `start` re-indented with two spaces; every string and
// literal token is copied as received. Returns the new text and where the value ends.
function indent(text, start, depth) {
  const first = text[start]
  if (first !== '{' && first !== '[') {
    const end = skipValue(text, start)
    return { out: text.slice(start, end), end }
  }
  const close = first === '{' ? '}' : ']'
  const firstMember = skipWhitespace(text, start + 1)
  if (text[firstMember] === close) return { out: first + close, end: firstMember + 1 }
  const { parts, end } = indentMembers(text, firstMember, { isObject: first === '{', close, depth })
  return { out: [first, parts.join(',\n'), INDENT.repeat(depth) + close].join('\n'), end }
}

function indentWhole(text) {
  const start = skipWhitespace(text, 0)
  if (start >= text.length) throw notPretty()
  const value = indent(text, start, 0)
  if (skipWhitespace(text, value.end) !== text.length) throw notPretty()
  return value.out
}

const EMPTY_COMPOSITE = /^[{[]\s*[}\]]$/
const OPENERS = new Set(['{', '['])

// Whether a body is a scalar or an empty object or array, which has no indentation to change.
export function hasNothingToIndent(text) {
  const trimmed = text.trim()
  if (trimmed === '') return false
  return !OPENERS.has(trimmed[0]) || EMPTY_COMPOSITE.test(trimmed)
}

// Re-indents a JSON body with two spaces per level, as the manual prints a pretty body.
export function prettyJson(text) {
  let result
  try {
    result = indentWhole(text)
  } catch (error) {
    if (error instanceof TransformError) throw notPretty()
    throw error
  }
  if (result === text) throw new TransformError(hasNothingToIndent(text) ? PRETTY_NOTHING : PRETTY_INDENTED)
  return result
}

export const TRANSFORMS = { stripTraceId, stripGraphQlTraceId, elideData, prettyJson }

export function applyTransforms(text, names) {
  return (names ?? []).reduce((current, name) => {
    const transform = TRANSFORMS[name]
    if (!transform) throw new TransformError(`unknown transform '${name}'`)
    return transform(current)
  }, text)
}
