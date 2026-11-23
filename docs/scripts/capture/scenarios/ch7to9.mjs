// Chapters 7, 8 and 9, which share the Tutorials data set. Article S1 is read at version 0 in
// chapters 7 and 8 and updated once in chapter 9, so chapter 9 runs last; chapter 9 also trashes and
// restores S3.
import { STRIP_TRACE_ID, check, exchange, must, read, setup } from './steps.mjs'

const TRASH_ROUND_TRIP = 'ch9-trash-round-trip'
const S3 = '/api/items/article/{{articleS3Id}}'
const DELETED_VALUES = "Query parameter 'deleted' must be exclude|only|with."
const DELETED_DENIED = 'Viewing deleted items requires delete permission.'

export const ch7 = {
  name: 'chapter 7: i18n',
  file: '07-i18n.md',
  steps: [
    exchange('ch7-create-missing-default-locale-400', 'POST', '/api/items/article', 400, {
      body: { status: 'draft' },
      transforms: STRIP_TRACE_ID,
    }),
    read('ch7-get-article-all-locales', '/api/items/article/{{articleS1Id}}'),
    read('ch7-get-article-locale-zhtw-empty', '/api/items/article/{{articleS2Id}}?locale=zh-TW'),
  ],
}

export const ch8 = {
  name: 'chapter 8: relations',
  file: '08-relations.md',
  steps: [
    read('ch8-deep-category-articles', '/api/items/category/{{tutorialsId}}?deep=articles'),
    read('ch8-deep-article-tags', '/api/items/article/{{articleS1Id}}?deep=tags'),
  ],
}

export const ch9 = {
  name: 'chapter 9: revisions and trash',
  file: '09-revisions-and-trash.md',
  steps: [
    setup('update article S1 once', 'PUT', '/api/items/article/{{articleS1Id}}', 200, { body: { status: 'draft' } }),
    read('ch9-list-revisions', '/api/items/article/{{articleS1Id}}/revisions'),
    exchange(TRASH_ROUND_TRIP, 'DELETE', S3, 204),
    read(TRASH_ROUND_TRIP, '/api/items/article?deleted=only&filter[id][_eq]={{articleS3Id}}'),
    exchange(TRASH_ROUND_TRIP, 'POST', `${S3}/restore`, 200),
    check('a misspelled deleted value', 'GET', '/api/items/article?deleted=bogus', 400, (json) =>
      must(json.error.message === DELETED_VALUES, 'the message lists the three accepted spellings'),
    ),
    check('an editor reads the trash', 'GET', '/api/items/article?deleted=only', 403, (json) =>
      must(json.error.message === DELETED_DENIED, 'the message says viewing deleted items needs delete permission'),
    { as: 'editor' }),
  ],
}
