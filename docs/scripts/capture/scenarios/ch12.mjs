// Chapter 12, REST conventions. Inside the chapter the order is the order article A changes in: the
// bump moves A to version 1 (so the stale write is a conflict) and the mixed tags write moves it to 2.
import { PASSWORD_FIELD, STRIP_TRACE_ID, check, exchange, must, setup } from './steps.mjs'

const ARTICLE_A = '/api/items/article/{{articleAId}}'
const LIST_ONE = '/api/items/article?limit=1'
const TOKEN_FLOW = 'ch12-token-flow'
const BEARER_NOTE = ' Authorization: Bearer {{token}}'
const FILLER = 'x'.repeat(201)

const noteOf = (json, name) => json.data.tags.find((tag) => tag.name === name)?._junction.note

// A block that shows the response only; the request is described by the prose.
const responseOnly = (capture, method, path, expect, body) =>
  exchange(capture, method, path, expect, { body, transforms: STRIP_TRACE_ID, print: { responseOnly: true } })

const bearerRead = (expect, extra) => ({
  capture: TOKEN_FLOW,
  method: 'GET',
  path: LIST_ONE,
  bearer: '{{token}}',
  expect,
  ...extra,
})

export const ch12 = {
  name: 'chapter 12: REST conventions',
  file: '12-rest-conventions.md',
  steps: [
    exchange('ch12-create-tag-201', 'POST', '/api/items/tag', 201, { body: { name: 'launch' }, print: { headers: true } }),
    exchange('ch12-users-validation-400', 'POST', '/api/users', 400, {
      body: JSON.stringify({ [PASSWORD_FIELD]: ['whatever', 123].join('') }),
      transforms: [...STRIP_TRACE_ID, 'prettyJson'],
    }),
    exchange('ch12-users-short-password-400', 'POST', '/api/users', 400, {
      body: JSON.stringify({ email: 'nobody@example.com', [PASSWORD_FIELD]: ['sho', 'rt'].join('') }),
      transforms: STRIP_TRACE_ID,
    }),
    setup('read the admin account', 'GET', '/api/auth/me', 200, { save: { adminId: 'data.id' } }),
    exchange(TOKEN_FLOW, 'POST', '/api/users/{{adminId}}/access-token', 200, {
      secret: { token: 'data.token' },
      print: { mask: { token: '<token>' } },
    }),
    bearerRead(200, { print: { note: BEARER_NOTE, mask: { token: '<token>' } } }),
    exchange(TOKEN_FLOW, 'DELETE', '/api/users/{{adminId}}/access-token', 204, {
      deletes: 'revokes the access token this run issued for the admin',
    }),
    bearerRead(401, { transforms: STRIP_TRACE_ID, print: { note: BEARER_NOTE, mask: { token: '<revoked token>' } } }),
    exchange('ch12-query-no-csrf-403', 'POST', '/api/items/article/query', 403, {
      body: '{}',
      csrf: false,
      transforms: STRIP_TRACE_ID,
      print: { note: '(no CSRF header)' },
    }),
    setup('bump article A to version 1', 'PUT', ARTICLE_A, 200, { body: { status: 'published' } }),
    exchange('ch12-put-version-conflict-409', 'PUT', ARTICLE_A, 409, {
      body: { status: 'published', version: 0 },
      transforms: STRIP_TRACE_ID,
    }),
    responseOnly('ch12-put-published-at-empty-400', 'PUT', ARTICLE_A, 400, { publishedAt: '' }),
    responseOnly('ch12-post-file-collection-400', 'POST', '/api/items/file', 400, {}),
    exchange('ch12-put-tags-mixed-200', 'PUT', ARTICLE_A, 200, {
      body: { tags: ['{{howtoId}}', { id: '{{releaseId}}', note: 'editor pick' }] },
    }),
    check('both links after the mixed write', 'GET', `${ARTICLE_A}?deep=tags`, 200, (json) =>
      must(noteOf(json, 'howto') === 'hero' && noteOf(json, 'release') === 'editor pick', 'the bare id keeps its note, the object sets its own'),
    ),
    responseOnly('ch12-put-tag-note-too-long-400', 'PUT', ARTICLE_A, 400, {
      tags: [{ id: '{{releaseId}}', note: FILLER }],
    }),
    responseOnly('ch12-put-tags-invalid-ids-400', 'PUT', ARTICLE_A, 400, { tags: [true] }),
  ],
}
