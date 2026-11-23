// Chapter 14, GraphQL. Two groups feed the file: the quantifier query has to read article A while it
// still holds exactly the howto/hero link (before chapter 12 adds a second link), and the mutation
// replaces that link, so it runs after chapters 12 and 13. The request bodies are the text the
// manual prints, spaces included.
import { anonymous, exchange } from './steps.mjs'

const GRAPHQL = '/graphql'
const GRAPHQL_TRACE = ['stripGraphQlTraceId']

const SOME_JUNCTION =
  '{"query": "query { articles(filter: { tags: { some: { name: { eq: \\"howto\\" }, junction: { note: { eq: \\"hero\\" } } } } }) { total items { id status tagsLinks { node { name } junction { note } } } } }"}'

function updateTagsLinks(vars) {
  const input = `tagsLinks: [{ id: \\"${vars.releaseId}\\", note: \\"editor pick\\" }]`
  return `{"query": "mutation { updateArticle(id: \\"${vars.articleAId}\\", input: { ${input} }) { id tagsLinks { node { name } junction { note } } } }"}`
}

export const ch14First = {
  name: 'chapter 14: the quantifier query',
  file: '14-graphql.md',
  steps: [exchange('ch14-some-junction', 'POST', GRAPHQL, 200, { body: SOME_JUNCTION })],
}

export const ch14Rest = {
  name: 'chapter 14: mutation and errors',
  file: '14-graphql.md',
  steps: [
    exchange('ch14-update-tagslinks', 'POST', GRAPHQL, 200, { body: updateTagsLinks }),
    anonymous('ch14-anon-users', 'POST', GRAPHQL, 200, {
      body: '{"query":"{ users { total } }"}',
      transforms: GRAPHQL_TRACE,
      print: { headers: true, note: '(no cookie)' },
    }),
    anonymous('ch14-bogus-field-400', 'POST', GRAPHQL, 400, {
      body: '{"query": "query { bogusThing { id } }"}',
      print: { headers: true },
    }),
  ],
}
