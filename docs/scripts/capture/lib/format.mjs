// Formats one HTTP exchange in the manual's transcript format:
//
//   $ METHOD path [note]
//   body:                      only when the request had a body
//   <request body as sent>
//   HTTP/1.1 200 OK            only when headers are shown, then the header lines and a blank line
//   <response body>            an empty body prints an empty line
//   HTTP_STATUS:200
//
// A binary response prints the declared summary lines in place of the body. With pretty the raw
// line stays and `pretty:` plus an indented copy follows the status line.
// A response-only exchange leaves out everything above the response. Nothing here knows which
// chapter an exchange belongs to.

import { prettyJson } from './transforms.mjs'

// The response headers a block shows: all of them (selection true) or the named ones, in server
// order. A named header the response lacks is an error: a declared setting that shows nothing would
// print something other than what the step says it prints.
export function shownHeaders(response, selection) {
  if (!Array.isArray(selection)) return response.headers
  const wanted = new Set(selection.map((name) => name.toLowerCase()))
  const present = new Set(response.headers.map(([name]) => name.toLowerCase()))
  const absent = selection.filter((name) => !present.has(name.toLowerCase()))
  if (absent.length > 0) throw new Error(`the response has no header named ${absent.join(', ')}`)
  return response.headers.filter(([name]) => wanted.has(name.toLowerCase()))
}

function headerLines(response, selection) {
  const shown = shownHeaders(response, selection).map(([name, value]) => `${name}: ${value}`)
  return [`HTTP/${response.httpVersion} ${response.status} ${response.statusMessage}`, ...shown, '']
}

export function formatExchange({ method, path, printPath, note, requestBody, headers, responseOnly, pretty, binary, response }) {
  const lines = []
  if (!responseOnly) {
    const suffix = note ? ` ${note}` : ''
    lines.push(`$ ${method} ${printPath ?? path}${suffix}`)
    if (requestBody !== undefined) lines.push('body:', requestBody)
  }
  if (headers) lines.push(...headerLines(response, headers))
  lines.push(...(binary ?? [response.body]), `HTTP_STATUS:${response.status}`)
  if (pretty) lines.push('pretty:', prettyJson(response.body))
  return lines.join('\n')
}

// A block that holds several exchanges separates them with a blank line.
export function formatBlock(exchanges) {
  return exchanges.join('\n\n')
}
