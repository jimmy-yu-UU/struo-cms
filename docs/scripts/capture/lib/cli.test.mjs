import assert from 'node:assert/strict'
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { test } from 'node:test'
import { cliBlock, cliDescriptors, readCliResult } from './cli.mjs'
import { CaptureError } from './errors.mjs'

const PROGRAM = 'dotnet Host.dll'
const OUT = 'demo-cli.out'
const EXIT = 'demo-cli.exit'
const step = (over = {}) => ({
  capture: 'demo-cli',
  cli: { program: PROGRAM, args: ['cmd', '--flag', 'v'], env: { A: 'b' }, exit: 0, ...over },
})
const groups = (steps) => [{ name: 'g', file: '01-demo.md', steps }]

function withDir(files, fn) {
  const dir = mkdtempSync(join(tmpdir(), 'struo-cli-results-'))
  try {
    for (const [name, content] of Object.entries(files)) writeFileSync(join(dir, name), content)
    return fn(dir)
  } finally {
    rmSync(dir, { recursive: true, force: true })
  }
}

test('the descriptors list every command step with its id, file, arguments, environment and exit code', () => {
  const list = cliDescriptors(groups([{ method: 'GET', path: '/x', expect: 200 }, step()]))
  assert.deepEqual(list, [
    { id: 'demo-cli', file: '01-demo.md', program: PROGRAM, args: ['cmd', '--flag', 'v'], env: { A: 'b' }, exit: 0 },
  ])
})

test('a step names its own file; an argument with white space or a missing exit code is refused', () => {
  assert.equal(cliDescriptors(groups([{ ...step(), file: '02-x.md' }]))[0].file, '02-x.md')
  assert.throws(() => cliDescriptors(groups([step({ args: ['a b'] })])), /white space/)
  assert.throws(() => cliDescriptors(groups([step({ exit: undefined })])), /exit code/)
  assert.throws(() => cliDescriptors(groups([step({ program: '' })])), /program/)
})

test('a result is read from <id>.out and <id>.exit', () => {
  const [descriptor] = cliDescriptors(groups([step()]))
  withDir({ [OUT]: 'line one\nline two\n', [EXIT]: '0\n' }, (dir) => {
    assert.equal(readCliResult(dir, descriptor), 'line one\nline two')
  })
})

test('a missing output file, a missing exit file and a different exit code each refuse the run', () => {
  const [descriptor] = cliDescriptors(groups([step()]))
  withDir({ [EXIT]: '0\n' }, (dir) => assert.throws(() => readCliResult(dir, descriptor), /demo-cli\.out/))
  withDir({ [OUT]: 'x\n' }, (dir) => assert.throws(() => readCliResult(dir, descriptor), /demo-cli\.exit/))
  withDir({ [OUT]: 'x\n', [EXIT]: '1\n' }, (dir) =>
    assert.throws(() => readCliResult(dir, descriptor), /exit code 1, the scenario declares 0/),
  )
  withDir({ [OUT]: 'x\n', [EXIT]: 'zero' }, (dir) => assert.throws(() => readCliResult(dir, descriptor), CaptureError))
})

test('an empty output and an output with a carriage return are refused', () => {
  const [descriptor] = cliDescriptors(groups([step()]))
  withDir({ [OUT]: '\n', [EXIT]: '0' }, (dir) => assert.throws(() => readCliResult(dir, descriptor), /no output/))
  withDir({ [OUT]: 'a\r\nb\n', [EXIT]: '0' }, (dir) => assert.throws(() => readCliResult(dir, descriptor), /carriage return/))
})

test('a block is the command line as printed, then the output', () => {
  const [descriptor] = cliDescriptors(groups([step()]))
  assert.equal(cliBlock(descriptor, 'done'), '$ dotnet Host.dll cmd --flag v\ndone')
})
