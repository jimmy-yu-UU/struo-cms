// The data set of chapters 7 to 9: category Tutorials and the articles S1, S2 and S3, all drafts with
// no publication date. They are created after every chapter that counts or filters the Guides
// articles has been captured, so those counts and filters never see them. Setup only.
import { create } from './steps.mjs'

export const tutorials = {
  name: 'seed: the Tutorials data set',
  steps: [
    create('create category Tutorials', 'category', { name: 'Tutorials' }, { tutorialsId: 'data.id' }),
    create('create article S1', 'article', {
      status: 'draft',
      categoryId: '{{tutorialsId}}',
      tags: [{ id: '{{howtoId}}', note: 'primary tag' }],
      translations: {
        en: { title: 'Getting started with StruoCMS', body: '<p>Run the API and the admin SPA.</p>' },
        'zh-TW': { title: '開始使用 StruoCMS', body: '<p>先把 API 與後台跑起來。</p>' },
      },
    }, { articleS1Id: 'data.id' }),
    create('create article S2', 'article', {
      status: 'draft',
      translations: { en: { title: 'Untranslated draft' } },
    }, { articleS2Id: 'data.id' }),
    create('create article S3', 'article', {
      status: 'draft',
      translations: { en: { title: 'Scratch article' } },
    }, { articleS3Id: 'data.id' }),
  ],
}
