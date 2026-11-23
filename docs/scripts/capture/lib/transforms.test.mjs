import assert from 'node:assert/strict'
import { test } from 'node:test'
import { applyTransforms, elideData, stripTraceId } from './transforms.mjs'

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
  const inner = `a ,\\"traceId\\":\\"${TRACE}\\" b`
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
  assert.throws(() => elideData('{"meta":{"data":[1]},"note":"\\"data\\":[1]"}'), /no top-level data/)
})

test('elideData handles an empty array and brackets inside strings', () => {
  assert.equal(elideData('{"data":[],"meta":{}}'), '{"data":[...],"meta":{}}')
  assert.equal(elideData('{"data":["a\\"]","]"],"meta":{}}'), '{"data":[...],"meta":{}}')
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
