// Chapter 10, query basics. Every exchange is a read by the admin session; the three error
// responses drop the traceId member, as the manual states.
const STRIP_TRACE_ID = ['stripTraceId']
const read = (capture, path, extra = {}) => ({ capture, as: 'admin', method: 'GET', path, expect: 200, ...extra })

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
  ],
}
