// Chapter 13, REST endpoints. The login runs under its own identity, a jar that holds no cookie: a
// request that presents a session cookie needs the CSRF header and revokes that session.
import { PASSWORD_FIELD, exchange } from './steps.mjs'

const LOGIN_IDENTITY = 'adminLogin'
const PLACEHOLDER = '<your-admin-password>'
const TRASH_AND_RESTORE = 'ch13-trash-and-restore'
const LOGIN_AND_ME = 'ch13-login-and-me'

export const ch13 = {
  name: 'chapter 13: REST endpoints',
  file: '13-rest-endpoints.md',
  steps: [
    exchange(TRASH_AND_RESTORE, 'DELETE', '/api/items/article/{{articleCId}}', 204),
    exchange(TRASH_AND_RESTORE, 'POST', '/api/items/article/{{articleCId}}/restore', 200),
    exchange('ch13-login-locks-query', 'POST', '/api/login-locks/query', 200, {
      body: { emails: ['admin@admin.com', 'nobody@example.com'] },
    }),
    {
      capture: LOGIN_AND_ME,
      as: LOGIN_IDENTITY,
      method: 'POST',
      path: '/api/auth/login',
      body: (vars) => JSON.stringify({ email: vars.adminEmail, [PASSWORD_FIELD]: vars.adminPassword }),
      expect: 200,
      print: {
        headers: true,
        requestBody: (vars) => JSON.stringify({ email: vars.adminEmail, [PASSWORD_FIELD]: PLACEHOLDER }),
        mask: { [`session_${LOGIN_IDENTITY}`]: '…' },
      },
    },
    { capture: LOGIN_AND_ME, as: LOGIN_IDENTITY, method: 'GET', path: '/api/auth/me', expect: 200 },
  ],
}
