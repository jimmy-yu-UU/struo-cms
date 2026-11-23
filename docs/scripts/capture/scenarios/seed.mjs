// The data set the query chapters read: category Guides, tags howto and release, and articles A, B
// and C created in that order (the default list order is newest first, so the manual shows C, B, A).
// Every step here is setup; nothing is printed. The runner's fresh-database guard has already logged
// in as the admin, so the steps use that session.
const GUIDES = '{{guidesId}}'
const create = (name, collection, body, save) => ({
  name,
  as: 'admin',
  method: 'POST',
  path: `/api/items/${collection}`,
  body,
  expect: 201,
  save,
})

export const seed = {
  name: 'seed: the Guides data set',
  steps: [
    create('create category Guides', 'category', { name: 'Guides' }, { guidesId: 'data.id' }),
    create('create tag howto', 'tag', { name: 'howto' }, { howtoId: 'data.id' }),
    create('create tag release', 'tag', { name: 'release' }, { releaseId: 'data.id' }),
    create('create article A', 'article', {
      status: 'published',
      publishedAt: '2026-03-01T00:00:00Z',
      categoryId: GUIDES,
      tags: [{ id: '{{howtoId}}', note: 'hero' }],
      translations: { en: { title: 'Getting started' }, 'zh-TW': { title: '開始使用' } },
    }, { articleAId: 'data.id' }),
    create('create article B', 'article', {
      status: 'published',
      publishedAt: '2026-06-01T00:00:00Z',
      categoryId: GUIDES,
      tags: ['{{howtoId}}', { id: '{{releaseId}}', note: 'hero' }],
      translations: { en: { title: 'Release notes' } },
    }, { articleBId: 'data.id' }),
    create('create article C', 'article', {
      status: 'draft',
      categoryId: GUIDES,
      translations: { en: { title: 'Draft piece' } },
    }, { articleCId: 'data.id' }),
  ],
}
