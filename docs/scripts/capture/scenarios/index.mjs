// The chapters the tool can capture, in the order they run. Chapters that mutate the shared data
// set must come after every chapter that reads it unchanged.
import { ch10 } from './ch10.mjs'
import { ch11 } from './ch11.mjs'

export { SEED } from './seed.mjs'
export const CHAPTERS = [ch10, ch11]
