// The data set the chapters read: category Guides, tags howto and release, articles A, B and C
// created in that order (the default list order is newest first, so the manual shows C, B, A), and
// the editor role (read on article only) with one user who holds it. Every step here is setup;
// nothing is printed. The runner's fresh-database guard has already logged in as the admin, so the
// steps use that session. The password of the editor user is a run variable and a registered secret.
import { PASSWORD_FIELD, create, setup } from './steps.mjs'

const GUIDES = '{{guidesId}}'
const EDITOR_EMAIL = 'editor@example.com'

export const seed = {
  name: 'seed: the Guides data set and the editor',
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
    create('create role editor', 'role', { name: 'editor' }, { editorRoleId: 'data.id' }),
    setup('grant the editor read on article', 'PUT', '/api/roles/{{editorRoleId}}/permissions', 200, {
      body: [{ collection: 'article', canRead: true, canWrite: false, canDelete: false }],
    }),
    setup('create the editor user', 'POST', '/api/users', 201, {
      body: (vars) => JSON.stringify({ email: EDITOR_EMAIL, name: 'Editor', [PASSWORD_FIELD]: vars.editorPassword }),
      save: { editorId: 'data.id' },
    }),
    create('give the editor user the editor role', 'userRole', { userId: '{{editorId}}', roleId: '{{editorRoleId}}' }),
  ],
}
