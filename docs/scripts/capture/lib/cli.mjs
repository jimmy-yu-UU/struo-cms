// Command-line blocks. The tool does not run commands: a scenario step declares the command, the
// operator runs it where the host runs and saves the merged output and the exit code as
// <id>.out and <id>.exit in a results directory, and the tool reads those files. Nothing here
// knows which command a chapter shows.
import { existsSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { CaptureError } from './errors.mjs'

function describe(step, file) {
  const { program, args, env = {}, exit } = step.cli
  if (typeof program !== 'string' || program.trim() === '') throw new CaptureError(`${step.capture}: the command step names no program`)
  if (!Array.isArray(args) || args.some((arg) => typeof arg !== 'string' || arg === '' || /\s/.test(arg))) {
    throw new CaptureError(`${step.capture}: command arguments must be non-empty and hold no white space`)
  }
  if (!Number.isInteger(exit)) throw new CaptureError(`${step.capture}: the command step declares no expected exit code`)
  return { id: step.capture, file, program, args, env, exit }
}

// Every command step of the run, in run order.
export function cliDescriptors(groups) {
  return groups.flatMap((group) =>
    group.steps.filter((step) => step.cli).map((step) => describe(step, step.file ?? group.file)),
  )
}

function readFile(dir, name, id) {
  const path = join(dir, name)
  if (!existsSync(path)) throw new CaptureError(`${id}: the command has no result file ${name} in ${dir}`)
  return readFileSync(path, 'utf8')
}

// The output of one command, checked against its declared exit code.
export function readCliResult(dir, descriptor) {
  const { id, exit } = descriptor
  const output = readFile(dir, `${id}.out`, id)
  const code = readFile(dir, `${id}.exit`, id).trim()
  if (!/^\d+$/.test(code)) throw new CaptureError(`${id}: ${id}.exit does not hold an exit code`)
  if (Number(code) !== exit) throw new CaptureError(`${id}: the command ended with exit code ${code}, the scenario declares ${exit}`)
  if (output.includes('\r')) throw new CaptureError(`${id}: the output contains a carriage return`)
  const text = output.replace(/\n$/, '')
  if (text.trim() === '') throw new CaptureError(`${id}: the command produced no output`)
  return text
}

export function cliBlock(descriptor, text) {
  return `$ ${descriptor.program} ${descriptor.args.join(' ')}\n${text}`
}
