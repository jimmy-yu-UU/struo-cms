// Chapter 14, GraphQL. Two groups feed the file: the quantifier query has to read article A while it
// still holds exactly the howto/hero link (before chapter 12 adds a second link), and the mutation
// replaces that link, so it runs after chapters 12 and 13. The request bodies are the text the
// manual prints, spaces included.
import { anonymous, check, exchange, must } from './steps.mjs'

const GRAPHQL = '/graphql'
const GRAPHQL_TRACE = ['stripGraphQlTraceId']
// Both error blocks drop the traceId from the body, so the body is shorter than the server's: the
// headers they show leave out Content-Length.
const HEADERS_WITHOUT_LENGTH = ['Content-Type', 'Date', 'Server', 'Vary', 'X-Content-Type-Options']

const SINGLE_ARTICLE =
  'query { article(id: "{{articleAId}}") { id status version category { name } tags(sort: ["name"], limit: 1) { name } } }'
const LIST_WITH_FACETS = [
  'query { articles(filter: { status: { eq: "published" } }, sort: ["-publishedAt"], limit: 1,',
  ' facets: ["status"], aggregate: { count: ["publishedAt"] })',
  ' { total facets { field values { value count } } aggregate } }',
].join('')
const NULL_WINS =
  'mutation { updateArticle(id: "{{articleAId}}", input: { tags: ["{{howtoId}}"], tagsLinks: null }) { id } }'
const FACETS = '[{"field":"status","values":[{"value":"published","count":2},{"value":"draft","count":1}]}]'

// The article after chapter 12 and the mutation: three writes, so version 3, and only the release link.
function sampleMatches(json, vars) {
  const article = { id: vars.articleAId, status: 'published', version: 3, category: { name: 'Guides' }, tags: [{ name: 'release' }] }
  must(JSON.stringify(json) === JSON.stringify({ data: { article } }), 'the single-article sample')
}

const ACCEPT_ANY = '*/*'
const ACCEPT_JSON = 'application/json'

const DEPTH_LIMIT = 12
const ALIAS_BOMB = 50

// categories -> items -> parent x n -> name: every field level counts, so the depth is n + 3.
function nestedParents(parents) {
  return '{ categories { items { ' + 'parent { '.repeat(parents) + 'name' + ' }'.repeat(parents) + ' } } }'
}

// The same costly list field under many aliases (the amplification the cost rule exists for).
function aliasBomb(count) {
  return '{ ' + Array.from({ length: count }, (_, index) => 'a' + index + ': articles { items { id } }').join(' ') + ' }'
}

// The request-level errors the prose describes. They fail before any resolver runs, so an anonymous
// caller sees them: the validation does not look at who asks.
const REQUEST_LEVEL_QUERIES = [
  { label: 'a document that does not parse', query: 'query {', assertion: parserCode },
  { label: 'an unknown field', query: 'query { bogusThing { id } }', assertion: noCodeErrors },
  { label: 'a document over the depth limit', query: nestedParents(DEPTH_LIMIT - 2), assertion: depthExceeded },
  { label: 'a document over the cost limit', query: aliasBomb(ALIAS_BOMB), assertion: costExceeded },
]

// The same anonymous request under a chosen Accept header: only the status depends on it.
const acceptCheck = (name, accept, query, expect, assertion) =>
  anonymous(undefined, 'POST', GRAPHQL, expect, { name, headers: { Accept: accept }, body: spaced(query), verify: assertion })

function noData(json) {
  must(!('data' in json), 'a request-level failure has no data key')
}

// The depth error has no code. Writing the nesting along a self-referencing relation (parent) also
// trips the parser's coordinate-cycle rule, which adds a second error with its own code.
function depthExceeded(json) {
  noData(json)
  const [first, cycle, ...rest] = json.errors
  must(first.extensions.code === undefined, 'the depth error has no extensions.code')
  must(rest.length === 0 && cycle.extensions.code === 'HC0087', 'a self-referencing nesting adds one HC0087 error')
  must(cycle.message === 'Maximum allowed coordinate cycle depth was exceeded.', 'the cycle message')
  must(first.extensions.allowedExecutionDepth === DEPTH_LIMIT, 'the error reports the allowed depth')
  must(first.extensions.detectedExecutionDepth === DEPTH_LIMIT + 1, 'every field level counts: 10 parents is depth 13')
  must(/execution depth of 13 which exceeds the max allowed execution depth of 12/.test(first.message), 'the depth message')
}

function costExceeded(json) {
  noData(json)
  const [first] = json.errors
  must(json.errors.length === 1 && first.extensions.code === 'HC0047', 'the cost error carries HC0047')
  must(first.message === 'The maximum allowed field cost was exceeded.', 'the cost message')
  must(first.extensions.fieldCost > first.extensions.maxFieldCost, 'the measured cost is above the limit')
}

function noCodeErrors(json) {
  noData(json)
  must(Array.isArray(json.errors) && json.errors.length > 0, 'a request-level failure answers with an errors array')
  must(json.errors.every((error) => error.extensions?.code === undefined), 'a request-level failure has no extensions.code')
}

// A document that does not parse carries the parser's own code (HC0011 and its kin), not one of the
// stable codes of the chapter.
function parserCode(json) {
  noData(json)
  must(Array.isArray(json.errors) && json.errors.length > 0, 'a parse failure answers with an errors array')
  must(json.errors.every((error) => /^HC\d+$/.test(error.extensions?.code ?? '')), 'a parse failure carries an HC code')
}

function codedError(json) {
  must(json.errors.every((error) => typeof error.extensions?.code === 'string'), 'a resolver error carries extensions.code')
}

// The mutation replaces the whole set of links: after it only release is linked, with its note.
function onlyReleaseLinked(json) {
  const links = json.data.tags.map((tag) => `${tag.name}:${tag._junction.note}`)
  must(links.join() === 'release:editor pick', 'the mutation leaves only the release link, so the howto link is gone')
}

// The request bodies the manual prints: one JSON object with a `query` member. Three blocks print a
// space after the colon, the anonymous one prints none; the quoting inside the query is JSON's own.
const spaced = (query) => `{"query": ${JSON.stringify(query)}}`
const compact = (query) => `{"query":${JSON.stringify(query)}}`

const SOME_JUNCTION = spaced(
  [
    'query { articles(filter: { tags: { some: { name: { eq: "howto" },',
    ' junction: { note: { eq: "hero" } } } } }) { total items { id status',
    ' tagsLinks { node { name } junction { note } } } } }',
  ].join(''),
)

function updateTagsLinks(vars) {
  const links = `[{ id: "${vars.releaseId}", note: "editor pick" }]`
  const mutation = `updateArticle(id: "${vars.articleAId}", input: { tagsLinks: ${links} })`
  return spaced(`mutation { ${mutation} { id tagsLinks { node { name } junction { note } } } }`)
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
    check('the links after the mutation', 'GET', '/api/items/article/{{articleAId}}?deep=tags', 200, onlyReleaseLinked),
    anonymous('ch14-anon-users', 'POST', GRAPHQL, 200, {
      body: compact('{ users { total } }'),
      transforms: GRAPHQL_TRACE,
      print: { headers: HEADERS_WITHOUT_LENGTH, note: '(no cookie)' },
    }),
    anonymous('ch14-bogus-field-400', 'POST', GRAPHQL, 400, {
      body: spaced('query { bogusThing { id } }'),
      transforms: GRAPHQL_TRACE,
      print: { headers: HEADERS_WITHOUT_LENGTH },
    }),
    check('the single-article query of the schema section', 'POST', GRAPHQL, 200, sampleMatches, {
      body: { query: SINGLE_ARTICLE },
    }),
    check(
      'the list query with facets and aggregate',
      'POST',
      GRAPHQL,
      200,
      (json) => must(JSON.stringify(json.data.articles.facets) === FACETS && json.data.articles.total === 2, 'total 2, published 2 and draft 1'),
      { body: { query: LIST_WITH_FACETS } },
    ),
    check(
      'tagsLinks null wins over tags',
      'POST',
      GRAPHQL,
      200,
      (json, vars) => must(json.data.updateArticle.id === vars.articleAId, 'the mutation that sends tags and tagsLinks null succeeds'),
      { body: { query: NULL_WINS } },
    ),
    check('the links after tagsLinks null', 'GET', '/api/items/article/{{articleAId}}?deep=tags', 200, onlyReleaseLinked),
    ...REQUEST_LEVEL_QUERIES.flatMap(({ label, query, assertion }) => [
      acceptCheck(`${label} with Accept */*`, ACCEPT_ANY, query, 400, assertion),
      acceptCheck(`${label} with Accept application/json`, ACCEPT_JSON, query, 200, assertion),
    ]),
    acceptCheck('a resolver error with Accept application/json', ACCEPT_JSON, '{ users { total } }', 200, codedError),
  ],
}
