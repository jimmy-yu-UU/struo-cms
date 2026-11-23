// Chapter 10, query basics. Every exchange is a read by the admin session; the three error
// responses drop the traceId member, as the manual states. The checks at the end send the requests
// the prose describes without showing them.
import { check, must, read, STRIP_TRACE_ID } from './steps.mjs'

const ARTICLES = '/api/items/article'
const titles = (json) => json.data.map((row) => row.translations.en?.title).sort((a, b) => a.localeCompare(b))

export const ch10 = {
  name: 'chapter 10: query basics',
  file: '10-query-basics.md',
  steps: [
    read('ch10-filter-published-sort-limit', '/api/items/article?filter[status][_eq]=published&sort=-publishedAt&limit=1&offset=0'),
    read('ch10-filter-publishedat-null', '/api/items/article?filter[publishedAt][_null]=true'),
    read(
      'ch10-some-quantifier-junction',
      '/api/items/article?filter[tags._some.name][_eq]=howto&filter[tags._some._junction.note][_eq]=hero',
    ),
    read('ch10-sort-to-many-400', '/api/items/article?sort=tags.name', { expect: 400, transforms: STRIP_TRACE_ID }),
    read('ch10-unknown-field-400', '/api/items/article?filter[bogus][_eq]=x', { expect: 400, transforms: STRIP_TRACE_ID }),
    read('ch10-unknown-relation-400', '/api/items/article?filter[bogus.name][_eq]=x', {
      expect: 400,
      transforms: STRIP_TRACE_ID,
    }),
    check('the same filter with _null=false', 'GET', `${ARTICLES}?filter[publishedAt][_null]=false`, 200, (json, vars) => {
      must(json.meta.total === 1 && json.data[0].id === vars.articleCId, '_null=false returns the same row, total 1')
    }),
    check('_or in a JSON envelope', 'POST', `${ARTICLES}/query`, 200, (json) => must(json.meta.total === 3, '_or matches all three, total 3'), {
      body: { filter: { _or: [{ status: { _eq: 'draft' } }, { categoryId: { _eq: '{{guidesId}}' } }] } },
    }),
    check(
      'path segments hold separately',
      'GET',
      `${ARTICLES}?filter[tags.name][_eq]=howto&filter[tags._junction.note][_eq]=hero`,
      200,
      (json) => must(titles(json).join('|') === 'Getting started|Release notes', 'the per-segment form returns both articles'),
    ),
    check('a translatable filter in the default locale', 'GET', `${ARTICLES}?filter[title][_contains]=start`, 200, (json) =>
      must(json.meta.total === 1 && titles(json)[0] === 'Getting started', 'start matches one article'),
    ),
    check(
      'a translatable filter with an explicit locale',
      'GET',
      `${ARTICLES}?filter[title][_contains]=%E9%96%8B%E5%A7%8B&locale=zh-TW`,
      200,
      (json) =>
        must(
          json.meta.total === 1 && Object.keys(json.data[0].translations).join() === 'zh-TW',
          'the Chinese title matches one article and only zh-TW is returned',
        ),
    ),
    check('a to-one sort path', 'GET', `${ARTICLES}?sort=category.name`, 200, () => {}),
  ],
}
