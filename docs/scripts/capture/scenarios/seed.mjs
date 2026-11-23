// The data set the query chapters read: category Guides, tags howto and release, and articles A, B
// and C created in that order (the default list order is newest first, so the manual shows C, B, A).
// Every step here is setup; nothing is printed. The first steps refuse a database that already holds
// rows, so a second capture needs a freshly migrated database.
import { CaptureError } from '../lib/runner.mjs'

const refuseNonEmpty = (collection) => ({
  name: `check that ${collection} is empty`,
  as: 'admin',
  method: 'GET',
  path: `/api/items/${collection}?limit=1`,
  expect: 200,
  verify: (json) => {
    const total = json.meta?.total
    if (total !== 0) {
      throw new CaptureError(
        `the database already holds ${collection} rows (total ${total}); the capture needs a freshly migrated database that holds only the bootstrap admin`,
      )
    }
  },
})

const create = (name, collection, body, save) => ({
  name,
  as: 'admin',
  method: 'POST',
  path: `/api/items/${collection}`,
  body,
  expect: 201,
  save,
})

const article = (name, body) => create(name, 'article', body, { [`${name.slice(-1).toLowerCase()}Id`]: 'data.id' })

export const SEED = [
  {
    name: 'log in as the bootstrap admin',
    as: 'admin',
    method: 'POST',
    path: '/api/auth/login',
    body: (vars) => ({ email: vars.adminEmail, password: vars.adminPassword }),
    expect: 200,
  },
  refuseNonEmpty('article'),
  refuseNonEmpty('category'),
  refuseNonEmpty('tag'),
  create('create category Guides', 'category', { name: 'Guides' }, { guidesId: 'data.id' }),
  create('create tag howto', 'tag', { name: 'howto' }, { howtoId: 'data.id' }),
  create('create tag release', 'tag', { name: 'release' }, { releaseId: 'data.id' }),
  article('create article A', {
    status: 'published',
    publishedAt: '2026-03-01T00:00:00Z',
    categoryId: '{guidesId}',
    tags: [{ id: '{howtoId}', note: 'hero' }],
    translations: { en: { title: 'Getting started' }, 'zh-TW': { title: '開始使用' } },
  }),
  article('create article B', {
    status: 'published',
    publishedAt: '2026-06-01T00:00:00Z',
    categoryId: '{guidesId}',
    tags: ['{howtoId}', { id: '{releaseId}', note: 'hero' }],
    translations: { en: { title: 'Release notes' } },
  }),
  article('create article C', {
    status: 'draft',
    categoryId: '{guidesId}',
    translations: { en: { title: 'Draft piece' } },
  }),
]
