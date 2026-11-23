// Chapter 16, authentication. The login lock is probed with an email the run made up: ten failed
// attempts, the first refusal, and the second refusal that the manual prints with its headers. The
// steps run one after another because the window is counted per account. The two command blocks
// are run by the operator (lib/cli.mjs); the second needs the host without a Redis connection.
import { PASSWORD_FIELD, STRIP_TRACE_ID, anonymous, exchange, setup } from './steps.mjs'

const LOGIN = '/api/auth/login'
const TOKEN_FLOW = 'ch16-token-flow'
const LIST_ONE = '/api/items/article?limit=1'
const BEARER_NOTE = '(Authorization: Bearer {{editorToken}}, no cookie)'
const PROBE_EMAIL_PLACEHOLDER = '$TW'
const WRONG_VALUE = ['no', 'pe'].join('')
const FAILED_ATTEMPTS = 10
const QUIET_CONSOLE = { Struo__Logging__ConsoleEnabled: 'false' }
const UNLOCK = { program: 'dotnet Struo.Api.dll', args: ['auth:unlock', '--email', 'Admin@Example.com'] }

const probe = (vars) => JSON.stringify({ email: vars.lockEmail, [PASSWORD_FIELD]: WRONG_VALUE })
const failedAttempt = (index) => anonymous(undefined, 'POST', LOGIN, 401, { name: `failed login ${index + 1}`, body: probe })

const bearerRead = (expect, extra) => ({
  capture: TOKEN_FLOW,
  method: 'GET',
  path: LIST_ONE,
  bearer: '{{editorToken}}',
  expect,
  print: { note: BEARER_NOTE },
  ...extra,
})

export const ch16 = {
  name: 'chapter 16: authentication',
  file: '16-authentication.md',
  steps: [
    exchange(TOKEN_FLOW, 'POST', '/api/users/{{editorId}}/access-token', 200, {
      secret: { editorToken: 'data.token' },
      print: { mask: { editorToken: '<token>' } },
    }),
    bearerRead(200, { print: { note: BEARER_NOTE, mask: { editorToken: '<token>' } } }),
    exchange(TOKEN_FLOW, 'DELETE', '/api/users/{{editorId}}/access-token', 204),
    bearerRead(401, { transforms: STRIP_TRACE_ID, print: { note: BEARER_NOTE, mask: { editorToken: '<token>' } } }),
    ...Array.from({ length: FAILED_ATTEMPTS }, (_, index) => failedAttempt(index)),
    anonymous(undefined, 'POST', LOGIN, 429, { name: 'the first refused login', body: probe }),
    anonymous('ch16-login-429-after-lock', 'POST', LOGIN, 429, {
      body: probe,
      transforms: STRIP_TRACE_ID,
      print: {
        headers: true,
        note: '(-i, the attempt after the first 429)',
        requestBody: () => JSON.stringify({ email: PROBE_EMAIL_PLACEHOLDER, [PASSWORD_FIELD]: WRONG_VALUE }),
      },
    }),
    setup('another account signs in while the probe address is locked', 'POST', LOGIN, 200, {
      as: 'adminCheck',
      body: (vars) => JSON.stringify({ email: vars.adminEmail, [PASSWORD_FIELD]: vars.adminPassword }),
    }),
    { capture: 'ch16-cli-unlock-cleared', cli: { ...UNLOCK, env: QUIET_CONSOLE, exit: 0 } },
    { capture: 'ch16-cli-unlock-no-redis', cli: { ...UNLOCK, env: { ...QUIET_CONSOLE, Redis__ConnectionString: '' }, exit: 1 } },
  ],
}
