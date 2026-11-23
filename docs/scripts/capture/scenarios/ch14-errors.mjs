// Chapter 14 checks that print nothing: how the GraphQL endpoint answers the failures the prose
// describes. Request-level failures (a document that does not parse, an unknown field, a depth or
// cost violation, a self-reference nested too far) fail validation before any resolver runs, so an
// anonymous caller sees them; their status depends on the Accept header. The shared-validator checks
// run as the admin. Each statement the chapter makes about a status, a code or a message is asserted
// here, so the run fails when the behaviour changes.
import { anonymous, check, must, setup } from './steps.mjs'

const GRAPHQL = '/graphql'
const ACCEPT_ANY = '*/*'
const ACCEPT_JSON = 'application/json'
const NO_ACCEPT = undefined
const BAD_USER_INPUT = 'BAD_USER_INPUT'

// Limits the manual states.
const DEPTH_LIMIT = 12
const FIELD_COST_LIMIT = 150
const MAX_OFFSET = 100000
const MAX_RELATION_LINKS = 1000
const MAX_IN_VALUES = 500
// categories -> items -> parent x n -> name: every field level counts, so the depth is n + 3.
const LEVELS_AROUND_PARENTS = 3
const PARENTS_OVER_DEPTH = DEPTH_LIMIT - LEVELS_AROUND_PARENTS + 1
const DETECTED_DEPTH = PARENTS_OVER_DEPTH + LEVELS_AROUND_PARENTS
// A path may repeat one self-referencing field three times; the fourth is refused.
const PARENTS_ALLOWED = 3
const ALIAS_BOMB = 50
const ALIAS_BOMB_COST = 550

const DEPTH_MESSAGE =
  `The GraphQL document has an execution depth of ${DETECTED_DEPTH} which exceeds ` +
  `the max allowed execution depth of ${DEPTH_LIMIT}.`
const COST_MESSAGE = 'The maximum allowed field cost was exceeded.'
const CYCLE_MESSAGE = 'Maximum allowed coordinate cycle depth was exceeded.'
const PARSE_CODE = 'HC0011'
const COST_CODE = 'HC0047'
const CYCLE_CODE = 'HC0087'
const UNAUTHORIZED = 'UNAUTHORIZED'

function nestedParents(parents) {
  return '{ categories { items { ' + 'parent { '.repeat(parents) + 'name' + ' }'.repeat(parents) + ' } } }'
}

// The same costly list field under many aliases (the amplification the cost rule exists for).
function aliasBomb(count) {
  return '{ ' + Array.from({ length: count }, (_, index) => 'a' + index + ': articles { items { id } }').join(' ') + ' }'
}

const noData = (json) => must(!('data' in json), 'a request-level failure has no data key')
const codeOf = (error) => error.extensions?.code

function noCodeErrors(json) {
  noData(json)
  must(Array.isArray(json.errors) && json.errors.length > 0, 'a request-level failure answers with an errors array')
  must(json.errors.every((error) => codeOf(error) === undefined), 'an unknown field has no extensions.code')
}

function parseFailure(json) {
  noData(json)
  must(json.errors.length === 1 && codeOf(json.errors[0]) === PARSE_CODE, `a parse failure carries ${PARSE_CODE}`)
}

// The depth error has no code. Nesting along a self-referencing relation (parent) also trips the
// server's coordinate-cycle validation rule, which adds a second error with its own code.
function depthExceeded(json) {
  noData(json)
  const [first, cycle, ...rest] = json.errors
  must(codeOf(first) === undefined, 'the depth error has no extensions.code')
  must(first.message === DEPTH_MESSAGE, 'the depth message')
  must(first.extensions.allowedExecutionDepth === DEPTH_LIMIT, 'the error reports the allowed depth')
  must(first.extensions.detectedExecutionDepth === DETECTED_DEPTH, 'every field level counts')
  must(rest.length === 0 && codeOf(cycle) === CYCLE_CODE && cycle.message === CYCLE_MESSAGE, 'one cycle error follows')
}

function costExceeded(json) {
  noData(json)
  const [first] = json.errors
  must(json.errors.length === 1 && codeOf(first) === COST_CODE, `the cost error carries ${COST_CODE}`)
  must(first.message === COST_MESSAGE, 'the cost message')
  must(first.extensions.fieldCost === ALIAS_BOMB_COST, 'the manual states the measured cost')
  must(first.extensions.maxFieldCost === FIELD_COST_LIMIT, 'the manual states the limit')
}

function cycleExceeded(json) {
  noData(json)
  must(json.errors.length === 1 && codeOf(json.errors[0]) === CYCLE_CODE, `the cycle error carries ${CYCLE_CODE}`)
  must(json.errors[0].message === CYCLE_MESSAGE, 'the cycle message')
}

// A resolver-level error: the document was valid, so `data` is present (null) and every error carries
// one of the chapter's stable codes. Here the anonymous caller may not read.
function resolverError(json) {
  must('data' in json && json.data === null, 'a resolver error answers with data null')
  must(json.errors.length > 0 && json.errors.every((error) => codeOf(error) === UNAUTHORIZED), 'UNAUTHORIZED')
}

const REQUEST_LEVEL = [
  { label: 'a document that does not parse', query: 'query {', assertion: parseFailure },
  { label: 'an unknown field', query: 'query { bogusThing { id } }', assertion: noCodeErrors },
  { label: 'a document over the depth limit', query: nestedParents(PARENTS_OVER_DEPTH), assertion: depthExceeded },
  { label: 'a document over the cost limit', query: aliasBomb(ALIAS_BOMB), assertion: costExceeded },
  { label: 'a self-reference nested four times', query: nestedParents(PARENTS_ALLOWED + 1), assertion: cycleExceeded },
]

// status by Accept: no header and */* answer 400, application/json answers 200.
const ACCEPTS = [
  { accept: NO_ACCEPT, label: 'no Accept', status: 400 },
  { accept: ACCEPT_ANY, label: 'Accept */*', status: 400 },
  { accept: ACCEPT_JSON, label: 'Accept application/json', status: 200 },
]

const anonymousQuery = (name, accept, query, expect, assertion) =>
  anonymous(undefined, 'POST', GRAPHQL, expect, {
    name,
    headers: accept === NO_ACCEPT ? undefined : { Accept: accept },
    body: JSON.stringify({ query }),
    verify: assertion,
  })

const requestLevel = REQUEST_LEVEL.flatMap(({ label, query, assertion }) =>
  ACCEPTS.map(({ accept, label: how, status }) => anonymousQuery(`${label} with ${how}`, accept, query, status, assertion)),
)

// Up to three nested parents pass validation; the anonymous caller then gets the resolver error.
const withinLimits = [1, 2, PARENTS_ALLOWED].flatMap((parents) =>
  [ACCEPT_ANY, ACCEPT_JSON].map((accept) =>
    anonymousQuery(`${parents} nested parent(s) with Accept ${accept}`, accept, nestedParents(parents), 200, resolverError),
  ),
)

const resolverLevel = ACCEPTS.map(({ accept, label }) =>
  anonymousQuery(`a resolver error with ${label}`, accept, '{ users { total } }', 200, resolverError),
)

// The shared validator and the write rules, reached through GraphQL as the admin.
function sharedError(message) {
  return (json) => {
    must('data' in json && (json.data === null || Object.values(json.data).every((value) => value === null)), 'every data value is null')
    const [first] = json.errors
    must(json.errors.length === 1 && codeOf(first) === BAD_USER_INPUT, 'one BAD_USER_INPUT error')
    must(first.message.startsWith(message), `the message starts with: ${message}`)
  }
}

const asAdmin = (name, query, assertion, expect = 200) =>
  check(name, 'POST', GRAPHQL, expect, assertion, { body: typeof query === 'function' ? query : { query } })

const OFFSET_MESSAGE = `Offset ${MAX_OFFSET + 1} exceeds the maximum of ${MAX_OFFSET}; narrow the result set`
const NESTED_OFFSET_MESSAGE = `Nested 'offset' ${MAX_OFFSET + 1} for relation 'articles' on 'category' exceeds the maximum`
const LINKS_MESSAGE = `'tags' accepts at most ${MAX_RELATION_LINKS} ids (Query:MaxRelationLinks).`
const UNIQUE_IDS = Array.from({ length: MAX_RELATION_LINKS + 1 }, (_, index) => '00000000-0000-0000-0000-' + String(index).padStart(12, '0'))
const IN_VALUES = Array.from({ length: MAX_IN_VALUES + 1 }, (_, index) => 'v' + index)
const IN_MESSAGE = `Too many values in _in/_nin filters (max ${MAX_IN_VALUES}, Query:MaxInValues).`
const tooManyLinks = (vars) =>
  JSON.stringify({
    query: `mutation { updateArticle(id: "${vars.articleAId}", input: { tags: ${JSON.stringify(UNIQUE_IDS)} }) { id } }`,
  })

const sharedValidator = [
  asAdmin('offset over the cap', `{ articles(offset: ${MAX_OFFSET + 1}) { total } }`, sharedError(OFFSET_MESSAGE)),
  asAdmin('nested offset over the cap', `{ categories { items { articles(offset: ${MAX_OFFSET + 1}) { id } } } }`, sharedError(NESTED_OFFSET_MESSAGE)),
  asAdmin('creating a user through the generic API', 'mutation { createUser(input: { email: "x@example.com" }) { id } }', sharedError("Items cannot be created in collection 'user' through the generic items API.")),
  asAdmin('creating a file through the generic API', 'mutation { createFile(input: {}) { id } }', sharedError("Items cannot be created in collection 'file' through the generic items API.")),
  asAdmin('more relation links than the limit', tooManyLinks, sharedError(LINKS_MESSAGE)),
  asAdmin(
    'more _in values than the limit',
    '{ articles(filter: { status: { in: ' + JSON.stringify(IN_VALUES) + ' } }) { total } }',
    sharedError(IN_MESSAGE),
  ),
  asAdmin('sort by an unknown field', '{ articles(sort: ["bogus"]) { total } }', sharedError("Unknown field 'bogus' on collection 'article'.")),
  asAdmin('sort by an unknown relation', '{ articles(sort: ["bogus.name"]) { total } }', sharedError("Unknown relation 'bogus' on 'article'")),
  asAdmin('sort across a to-many relation', '{ articles(sort: ["tags.name"]) { total } }', sharedError("Sort across to-many relations is not supported: 'tags.name'.")),
  asAdmin('a sort path past the relation depth', '{ articles(sort: ["category.parent.parent.parent.parent.parent.parent.parent.name"]) { total } }', sharedError('Relation path')),
  asAdmin('an unknown field in a typed filter', '{ articles(filter: { bogus: { eq: "x" } }) { total } }', noCodeErrors, 400),
]

// The same limits through REST answer 400.
const restCounterparts = [
  setup('REST: offset over the cap', 'GET', '/api/items/article?offset=' + (MAX_OFFSET + 1), 400),
  setup('POST .../query: offset over the cap', 'POST', '/api/items/article/query', 400, { body: { offset: MAX_OFFSET + 1 } }),
  setup('REST: more _in values than the limit', 'GET', '/api/items/article?filter[status][_in]=' + IN_VALUES.join(','), 400),
  setup('REST: creating a user through the generic API', 'POST', '/api/items/user', 400, { body: { email: 'x@example.com' } }),
  setup('REST: more relation links than the limit', 'PUT', '/api/items/article/{{articleAId}}', 400, { body: { tags: UNIQUE_IDS } }),
]

export const errorChecks = [...requestLevel, ...withinLimits, ...resolverLevel, ...sharedValidator, ...restCounterparts]
