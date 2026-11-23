import assert from 'node:assert/strict'
import { test } from 'node:test'
import { applyTransforms, elideData, prettyJson, stripGraphQlTraceId, stripTraceId } from './transforms.mjs'

const TRACE = 'a1b2c3d4e5f60718293a4b5c6d7e8f90'

test('stripTraceId removes the traceId member of error and nothing else', () => {
  const body = `{"success":false,"error":{"code":"BAD_USER_INPUT","message":"No.","traceId":"${TRACE}"}}`
  assert.equal(stripTraceId(body), '{"success":false,"error":{"code":"BAD_USER_INPUT","message":"No."}}')
})

test('stripTraceId keeps JSON escapes exactly as received', () => {
  const body = `{"success":false,"error":{"code":"F","message":"Missing required 'X' header.","traceId":"${TRACE}"}}`
  assert.equal(stripTraceId(body), '{"success":false,"error":{"code":"F","message":"Missing required \'X\' header."}}')
})

test('stripTraceId leaves a traceId text inside a string value alone', () => {
  const inner = String.raw`a ,\"traceId\":\"${TRACE}\" b`
  const body = `{"success":false,"error":{"code":"C","message":"${inner}","traceId":"${TRACE}"}}`
  assert.equal(stripTraceId(body), `{"success":false,"error":{"code":"C","message":"${inner}"}}`)
})

test('stripTraceId leaves a traceId outside error alone', () => {
  const body = `{"data":{"traceId":"${TRACE}"},"error":{"code":"C","traceId":"${TRACE}"},"traceId":"${TRACE}"}`
  assert.equal(stripTraceId(body), `{"data":{"traceId":"${TRACE}"},"error":{"code":"C"},"traceId":"${TRACE}"}`)
})

test('stripTraceId removes the member when it is first or alone', () => {
  assert.equal(stripTraceId(`{"error":{"traceId":"${TRACE}","code":"C"}}`), '{"error":{"code":"C"}}')
  assert.equal(stripTraceId(`{"error":{"traceId":"${TRACE}"}}`), '{"error":{}}')
})

test('stripTraceId fails when there is nothing to remove or the value is not a trace id', () => {
  assert.throws(() => stripTraceId('{"success":true,"data":[]}'), /no error object/)
  assert.throws(() => stripTraceId('{"error":{"code":"C"}}'), /no traceId/)
  assert.throws(() => stripTraceId('{"error":{"traceId":"short"}}'), /not a 32-digit hex/)
  assert.throws(() => stripTraceId('not json'), /not a JSON object/)
})

test('elideData replaces the top-level data array with [...] and touches the rest as received', () => {
  const body = '{"success":true,"data":[{"id":"1","data":[1,2]},{"s":"]["}],"meta":{"total":2,"data":[9]}}'
  assert.equal(elideData(body), '{"success":true,"data":[...],"meta":{"total":2,"data":[9]}}')
})

test('elideData ignores a data key nested deeper and one that appears only in a string', () => {
  assert.throws(() => elideData(String.raw`{"meta":{"data":[1]},"note":"\"data\":[1]"}`), /no top-level data/)
})

test('elideData handles an empty array and brackets inside strings', () => {
  assert.equal(elideData('{"data":[],"meta":{}}'), '{"data":[...],"meta":{}}')
  assert.equal(elideData(String.raw`{"data":["a\"]","]"],"meta":{}}`), '{"data":[...],"meta":{}}')
})

test('elideData fails when data is not an array', () => {
  assert.throws(() => elideData('{"data":{"a":1}}'), /not an array/)
})

test('applyTransforms runs the declared transforms in order and rejects an unknown name', () => {
  const body = `{"data":[1],"error":{"traceId":"${TRACE}"}}`
  assert.equal(applyTransforms(body, ['elideData', 'stripTraceId']), '{"data":[...],"error":{}}')
  assert.equal(applyTransforms('{"a":1}', []), '{"a":1}')
  assert.equal(applyTransforms('{"a":1}', undefined), '{"a":1}')
  assert.throws(() => applyTransforms('{}', ['prettify']), /unknown transform 'prettify'/)
})

test('prettyJson re-indents with two spaces and keeps every token as received', () => {
  const body = '{"success":false,"error":{"code":"V","message":"It\'s \u0027x\u0027","details":[{"field":"Email"}]},"empty":{},"none":[]}'
  assert.equal(
    prettyJson(body),
    [
      '{',
      '  "success": false,',
      '  "error": {',
      '    "code": "V",',
      '    "message": "It\'s \u0027x\u0027",',
      '    "details": [',
      '      {',
      '        "field": "Email"',
      '      }',
      '    ]',
      '  },',
      '  "empty": {},',
      '  "none": []',
      '}',
    ].join('\n'),
  )
})

test('prettyJson keeps brackets, commas and colons inside strings', () => {
  assert.equal(prettyJson('{"a":"x,y:{z}[w]"}'), '{\n  "a": "x,y:{z}[w]"\n}')
})

test('prettyJson fails when the text is not JSON or is already indented as it would be', () => {
  assert.throws(() => prettyJson('not json'), /prettyJson/)
  assert.throws(() => prettyJson('{"a":1'), /prettyJson/)
  assert.throws(() => prettyJson('{"a":1} trailing'), /prettyJson/)
  assert.throws(() => prettyJson('{\n  "a": 1\n}'), /changes nothing/)
})

test('prettyJson is a declared transform', () => {
  assert.equal(applyTransforms('{"a":[1]}', ['prettyJson']), '{\n  "a": [\n    1\n  ]\n}')
})

test('stripGraphQlTraceId removes the traceId of every error extensions object and nothing else', () => {
  const body = `{"errors":[{"message":"No.","path":["users"],"extensions":{"code":"UNAUTHORIZED","traceId":"${TRACE}"}},{"message":"B","extensions":{"traceId":"${TRACE}","x":1}}],"data":null}`
  assert.equal(
    stripGraphQlTraceId(body),
    '{"errors":[{"message":"No.","path":["users"],"extensions":{"code":"UNAUTHORIZED"}},{"message":"B","extensions":{"x":1}}],"data":null}',
  )
})

test('stripGraphQlTraceId leaves a traceId elsewhere alone', () => {
  const body = `{"errors":[{"message":"m","extensions":{"traceId":"${TRACE}"}}],"extensions":{"traceId":"${TRACE}"}}`
  assert.equal(stripGraphQlTraceId(body), `{"errors":[{"message":"m","extensions":{}}],"extensions":{"traceId":"${TRACE}"}}`)
})

test('stripGraphQlTraceId fails when there is nothing to remove', () => {
  assert.throws(() => stripGraphQlTraceId('{"data":{}}'), /no errors array/)
  assert.throws(() => stripGraphQlTraceId('{"errors":[{"message":"m","extensions":{"code":"X"}}]}'), /no error has a traceId/)
  assert.throws(() => stripGraphQlTraceId(`{"errors":[{"extensions":{"traceId":"short"}}]}`), /not a 32-digit hex/)
})

test('prettyJson names the true reason when the body has nothing to indent', () => {
  for (const body of ['{}', '[]', '7', '"text"', 'null']) {
    assert.throws(() => prettyJson(body), /nothing to indent/, body)
  }
})
