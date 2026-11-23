import assert from 'node:assert/strict'
import { test } from 'node:test'
import { inOrder } from './sequence.mjs'

test('each item starts only after the previous one has finished', async () => {
  const log = []
  const delays = [30, 5, 15]
  await inOrder(delays, async (delay, index) => {
    log.push(`start ${index}`)
    await new Promise((resolve) => setTimeout(resolve, delay))
    log.push(`end ${index}`)
  })
  assert.deepEqual(log, ['start 0', 'end 0', 'start 1', 'end 1', 'start 2', 'end 2'])
})

test('a failure stops the sequence and rejects with the failure', async () => {
  const seen = []
  await assert.rejects(
    inOrder([1, 2, 3], async (item) => {
      seen.push(item)
      if (item === 2) throw new Error('stop here')
    }),
    /stop here/,
  )
  assert.deepEqual(seen, [1, 2])
})

test('an empty list resolves', async () => {
  await inOrder([], async () => assert.fail('never called'))
})
