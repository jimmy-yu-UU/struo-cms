// Chapter 11, advanced queries. Two blocks elide the `data` array because the point is `meta`;
// the error responses drop the traceId member.
const read = (capture, path, extra = {}) => ({ capture, as: 'admin', method: 'GET', path, expect: 200, ...extra })

// category, then six nested `parent` levels: seven relation hops, one more than the default maximum.
function tooDeep() {
  let node = { deep: {} }
  for (let level = 0; level < 6; level += 1) node = { deep: { parent: node } }
  return { deep: { category: node } }
}

export const ch11 = {
  name: 'chapter 11: advanced queries',
  file: '11-query-advanced.md',
  steps: [
    read('ch11-fields-id-status', '/api/items/article?fields=id,status'),
    read('ch11-fields-relation-path-400', '/api/items/article?fields=category.name', {
      expect: 400,
      transforms: ['stripTraceId'],
    }),
    {
      capture: 'ch11-deep-too-deep-400',
      as: 'admin',
      method: 'POST',
      path: '/api/items/article/query',
      body: tooDeep(),
      expect: 400,
      transforms: ['stripTraceId'],
    },
    read(
      'ch11-facets-status',
      '/api/items/article?filter[categoryId][_eq]={{guidesId}}&filter[status][_eq]=published&facets=status',
      { transforms: ['elideData'] },
    ),
    read('ch11-aggregate-count-max', '/api/items/article?aggregate[count]=publishedAt&aggregate[max]=publishedAt', {
      transforms: ['elideData'],
    }),
    read(
      'ch11-full-example',
      '/api/items/article?filter[status][_eq]=published&sort=-publishedAt&limit=1&offset=0&fields=id,status&deep=category,tags&facets=status&aggregate[count]=publishedAt',
    ),
  ],
}
