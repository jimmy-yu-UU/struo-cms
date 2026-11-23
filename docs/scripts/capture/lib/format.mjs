// Formats one HTTP exchange in the manual's transcript format:
//
//   $ METHOD path [note]
//   body:                      only when the request had a body
//   <request body as sent>
//   HTTP/1.1 200 OK            only when headers are shown, then the header lines and a blank line
//   <response body>            an empty body prints an empty line
//   HTTP_STATUS:200
//
// A response-only exchange leaves out everything above the response. Nothing here knows which
// chapter an exchange belongs to.

function headerLines(response, selection) {
  const wanted = Array.isArray(selection) ? new Set(selection.map((name) => name.toLowerCase())) : null
  const shown = response.headers
    .filter(([name]) => wanted === null || wanted.has(name.toLowerCase()))
    .map(([name, value]) => `${name}: ${value}`)
  return [`HTTP/${response.httpVersion} ${response.status} ${response.statusMessage}`, ...shown, '']
}

export function formatExchange({ method, path, printPath, note, requestBody, headers, responseOnly, response }) {
  const lines = []
  if (!responseOnly) {
    const suffix = note ? ` ${note}` : ''
    lines.push(`$ ${method} ${printPath ?? path}${suffix}`)
    if (requestBody !== undefined) lines.push('body:', requestBody)
  }
  if (headers) lines.push(...headerLines(response, headers))
  lines.push(response.body, `HTTP_STATUS:${response.status}`)
  return lines.join('\n')
}

// A block that holds several exchanges separates them with a blank line.
export function formatBlock(exchanges) {
  return exchanges.join('\n\n')
}
