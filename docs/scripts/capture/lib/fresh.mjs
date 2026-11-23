// The fresh-database guard: a capture runs only against a freshly migrated database that holds the
// bootstrap admin and nothing else, so the printed ids, versions and totals come from the run's own
// data set and nothing the run changes belongs to someone else. It logs in as the admin first (the
// same session the steps use), then reads every list with trashed rows included (`deleted=with`).
//
// This is the last line of defence, not the isolation: a host that can reach a real database is
// refused only if that database happens to be empty. Pointing the host at a throwaway database is
// the operator's job (docs/ai/task-playbooks.md, Playbook 6).
import { CaptureError } from './errors.mjs'

async function total(client, collection) {
  const response = await client.request({
    identity: 'admin',
    method: 'GET',
    path: `/api/items/${collection}?limit=1&deleted=with`,
  })
  if (response.status !== 200) {
    throw new CaptureError(`fresh-database check: listing ${collection} answered HTTP ${response.status}`)
  }
  return JSON.parse(response.body).meta.total
}

export async function checkFresh({ client, vars, collections }) {
  const login = await client.request({
    identity: 'admin',
    method: 'POST',
    path: '/api/auth/login',
    body: JSON.stringify({ email: vars.adminEmail, password: vars.adminPassword }),
  })
  if (login.status !== 200) throw new CaptureError(`login as the bootstrap admin: HTTP ${login.status}`)

  const found = []
  const users = await total(client, 'user')
  if (users !== 1) found.push(`${users} users (expected exactly 1)`)
  for (const collection of ['file', ...collections]) {
    const rows = await total(client, collection)
    if (rows > 0) found.push(`${rows} ${collection} row(s) (trashed rows included)`)
  }
  if (found.length > 0) {
    throw new CaptureError(
      `the database is not freshly migrated: found ${found.join(', ')}; the capture needs a database that holds only the bootstrap admin`,
    )
  }
}
