// Chapter 17, roles and permissions. The editor signs in with a cookie session and sends the CSRF
// header, so the refusal printed is the permission check and not the CSRF check. The reads of tag
// are the unshown requests the prose describes; they run so the claim is checked against the host.
import { PASSWORD_FIELD, STRIP_TRACE_ID, anonymous, exchange, read, setup } from './steps.mjs'

const EDITOR = 'editor'
const EFFECTIVE = '/api/users/{{editorId}}/effective-permissions'
const EFFECTIVE_BLOCK = 'ch17-effective-permissions'

export const ch17 = {
  name: 'chapter 17: roles and permissions',
  file: '17-roles-and-permissions.md',
  steps: [
    {
      name: 'the editor signs in',
      as: EDITOR,
      method: 'POST',
      path: '/api/auth/login',
      body: (vars) => JSON.stringify({ email: 'editor@example.com', [PASSWORD_FIELD]: vars.editorPassword }),
      expect: 200,
    },
    {
      capture: 'ch17-put-article-editor-403',
      as: EDITOR,
      method: 'PUT',
      path: '/api/items/article/{{articleAId}}',
      body: { status: 'draft' },
      expect: 403,
      transforms: STRIP_TRACE_ID,
      print: { pretty: true },
    },
    { name: 'the editor reads tag', as: EDITOR, method: 'GET', path: '/api/items/tag', expect: 403 },
    anonymous(undefined, 'GET', '/api/items/tag', 401, { name: 'an anonymous caller reads tag' }),
    setup('look up the admin role', 'GET', '/api/items/role?filter[name][_eq]=admin', 200, { save: { adminRoleId: 'data.0.id' } }),
    read(EFFECTIVE_BLOCK, EFFECTIVE, { print: { pretty: true } }),
    read(EFFECTIVE_BLOCK, `${EFFECTIVE}?roles={{adminRoleId}}`, { print: { pretty: true } }),
  ],
}
