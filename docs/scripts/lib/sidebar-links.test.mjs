import assert from 'node:assert/strict'
import { test } from 'node:test'
import { firstLink } from './sidebar-links.mjs'

test('firstLink: on a mixed list returns the first top-level link', () => {
  const items = [
    { text: 'New One', link: '/en/01-new' },
    {
      text: 'Group',
      collapsed: true,
      items: [{ text: 'Old Two', link: '/en/02-old' }],
    },
  ]
  assert.equal(firstLink(items), '/en/01-new')
})

test('firstLink: on a list that is only a group returns the group\'s first item link', () => {
  const items = [
    {
      text: 'Group',
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
