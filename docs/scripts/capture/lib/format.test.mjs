import assert from 'node:assert/strict'
import { test } from 'node:test'
import { formatBlock, formatExchange } from './format.mjs'
const STATUS_200 = 'HTTP_STATUS:200'

const response = (over = {}) => ({
  status: 200,
  statusMessage: 'OK',
  httpVersion: '1.1',
  headers: [
    ['Content-Type', 'application/json; charset=utf-8'],
    ['Date', 'Mon, 23 Nov 2026 00:00:00 GMT'],
    ['Server', 'Kestrel'],
  ],
  body: '{"success":true}',
  ...over,
})

test('a bodyless exchange prints the request line, the raw response and the status', () => {
  const text = formatExchange({ method: 'GET', path: '/api/x?a=1', response: response() })
  assert.equal(text, '$ GET /api/x?a=1\n{"success":true}\nHTTP_STATUS:200')
})

test('a request body prints after a body: line, exactly as sent', () => {
  const sent = String.raw`{"a": 1,"b":"é"}`
  const text = formatExchange({
    method: 'POST',
    path: '/api/x',
    requestBody: sent,
    response: response({ status: 400, body: '{"e":1}' }),
  })
  assert.equal(text, `$ POST /api/x\nbody:\n${sent}\n{"e":1}\nHTTP_STATUS:400`)
})

test('a note follows the path after one space', () => {
  const text = formatExchange({ method: 'POST', path: '/q', note: '(no CSRF header)', requestBody: '{}', response: response() })
  assert.equal(text.split('\n')[0], '$ POST /q (no CSRF header)')
})

test('headers true prints the status line and every header in server order, then a blank line', () => {
  const text = formatExchange({ method: 'GET', path: '/x', headers: true, response: response() })
  assert.equal(
    text,
    [
      '$ GET /x',
      'HTTP/1.1 200 OK',
      'Content-Type: application/json; charset=utf-8',
      'Date: Mon, 23 Nov 2026 00:00:00 GMT',
      'Server: Kestrel',
      '',
      '{"success":true}',
      STATUS_200,
    ].join('\n'),
  )
})

test('a header list keeps only the named headers, compared without regard to case', () => {
  const text = formatExchange({ method: 'GET', path: '/x', headers: ['content-type'], response: response() })
  assert.equal(text.split('\n').slice(1, 4).join('|'), 'HTTP/1.1 200 OK|Content-Type: application/json; charset=utf-8|')
})

test('an empty response body prints a blank line before the status', () => {
  const text = formatExchange({ method: 'DELETE', path: '/x', response: response({ status: 204, body: '' }) })
  assert.equal(text, '$ DELETE /x\n\nHTTP_STATUS:204')
})

test('a response-only exchange has no request line and no body label', () => {
  const text = formatExchange({
    method: 'PUT',
    path: '/x',
    requestBody: '{"a":1}',
    responseOnly: true,
    response: response({ status: 400, body: '{"e":1}' }),
  })
  assert.equal(text, '{"e":1}\nHTTP_STATUS:400')
})

test('printPath replaces the path shown on the request line, not the one sent', () => {
  const text = formatExchange({ method: 'GET', path: '/x?t=SECRET', printPath: '/x?t=<token>', response: response() })
  assert.equal(text.split('\n')[0], '$ GET /x?t=<token>')
})

test('a block joins several exchanges with a blank line', () => {
  assert.equal(formatBlock(['a\nb', 'c']), 'a\nb\n\nc')
  assert.equal(formatBlock(['solo']), 'solo')
})

test('pretty appends a pretty: line and an indented copy after the status, leaving the raw line alone', () => {
  const text = formatExchange({
    method: 'GET',
    path: '/x',
    pretty: true,
    response: response({ body: '{"a":{"b":[1]},"c":{}}' }),
  })
  assert.equal(
    text,
    ['$ GET /x', '{"a":{"b":[1]},"c":{}}', STATUS_200, 'pretty:', '{', '  "a": {', '    "b": [', '      1', '    ]', '  },', '  "c": {}', '}'].join('\n'),
  )
})

test('a binary response prints its summary lines in place of the body, after the chosen headers', () => {
  const text = formatExchange({
    method: 'GET',
    path: '/img',
    headers: ['content-type'],
    binary: ['# body: 3 bytes (binary, not shown)'],
    response: response({ body: '���' }),
  })
  assert.equal(
    text,
    ['$ GET /img', 'HTTP/1.1 200 OK', 'Content-Type: application/json; charset=utf-8', '', '# body: 3 bytes (binary, not shown)', STATUS_200].join('\n'),
  )
})

test('a header list naming a header the response lacks is an error that names it', () => {
  assert.throws(
    () => formatExchange({ method: 'GET', path: '/x', headers: ['content-type', 'X-Missing'], response: response() }),
    /X-Missing/,
  )
})
