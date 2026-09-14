import assert from 'node:assert/strict'
import { test } from 'node:test'
import { firstLink, groupChapters } from './sidebar-groups.mjs'

const entry = (file, text, link) => ({ file, text, link })

test('groupChapters: no legacy entries yields an identical flat list, no group', () => {
  const entries = [
    entry('01-a.md', 'A', '/en/01-a'),
    entry('02-b.md', 'B', '/en/02-b'),
  ]
  const result = groupChapters(entries, new Set(), 'Legacy chapters')
  assert.deepEqual(result, [
    { text: 'A', link: '/en/01-a' },
    { text: 'B', link: '/en/02-b' },
  ])
})

test('groupChapters: mixed entries put non-legacy first, then one trailing collapsed group', () => {
  const entries = [
    entry('01-new.md', 'New One', '/en/01-new'),
    entry('02-old.md', 'Old Two', '/en/02-old'),
    entry('03-new.md', 'New Three', '/en/03-new'),
    entry('04-old.md', 'Old Four', '/en/04-old'),
  ]
  const legacy = new Set(['02-old.md', '04-old.md'])
  const result = groupChapters(entries, legacy, 'Legacy chapters (being rewritten)')

  assert.deepEqual(result, [
    { text: 'New One', link: '/en/01-new' },
    { text: 'New Three', link: '/en/03-new' },
    {
      text: 'Legacy chapters (being rewritten)',
      collapsed: true,
      items: [
        { text: 'Old Two', link: '/en/02-old' },
        { text: 'Old Four', link: '/en/04-old' },
      ],
    },
  ])
})

test('firstLink: on a mixed list returns the first top-level link', () => {
  const items = [
    { text: 'New One', link: '/en/01-new' },
    {
      text: 'Legacy',
      collapsed: true,
      items: [{ text: 'Old Two', link: '/en/02-old' }],
    },
  ]
  assert.equal(firstLink(items), '/en/01-new')
})

test('firstLink: on a list that is only a group returns the group\'s first item link', () => {
  const items = [
    {
      text: 'Legacy',
      collapsed: true,
      items: [
        { text: 'Old Two', link: '/en/02-old' },
        { text: 'Old Four', link: '/en/04-old' },
      ],
    },
  ]
  assert.equal(firstLink(items), '/en/02-old')
})

test('firstLink: throws on an empty list', () => {
  assert.throws(() => firstLink([]), /link/)
})
