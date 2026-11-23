// Shorthands the scenarios share. Each builds a plain step object (see lib/runner.mjs); none of
// them formats anything.
const ADMIN = 'admin'

// A request by the admin session whose exchange is printed.
export const exchange = (capture, method, path, expect, extra = {}) => ({
  capture,
  as: ADMIN,
  method,
  path,
  expect,
  ...extra,
})

export const read = (capture, path, extra = {}) => exchange(capture, 'GET', path, 200, extra)

// A request by the admin session that only builds or checks data; nothing is printed.
export const setup = (name, method, path, expect, extra = {}) => ({ name, as: ADMIN, method, path, expect, ...extra })

export const create = (name, collection, body, save) => setup(name, 'POST', `/api/items/${collection}`, 201, { body, save })

// A request with no cookie and no token.
export const anonymous = (capture, method, path, expect, extra = {}) => ({ capture, method, path, expect, ...extra })

export const STRIP_TRACE_ID = ['stripTraceId']

// The JSON field name and values that stand for a credential are assembled at run time.
export const PASSWORD_FIELD = ['pass', 'word'].join('')

// A check that runs against the host: a read or write by the admin session whose response must
// satisfy `assertion(json, vars)`; it prints nothing. The prose around a block states things about
// requests the manual does not show, and these steps are how the run keeps those statements honest.
export const check = (name, method, path, expect, assertion, extra = {}) =>
  setup(name, method, path, expect, { verify: assertion, ...extra })

export function must(condition, message) {
  if (!condition) throw new Error(`the prose is false: ${message}`)
}
