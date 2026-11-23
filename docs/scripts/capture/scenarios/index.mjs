// The run, as one ordered list of groups. Order is the order the shared data set is built and
// changed in: every group that reads the Guides data set unchanged comes before any group that
// mutates it. A file may be fed by several groups (a capture step or its group names the file).
import { ch10 } from './ch10.mjs'
import { ch11 } from './ch11.mjs'
import { seed } from './seed.mjs'

// Coupling: the runner's fresh-database guard (lib/fresh.mjs) logs in as the bootstrap admin under
// the identity named `admin`, and the steps with `as: 'admin'` use that session. A scenario that
// needs another login of its own adds a step for it under a different identity.
export const GROUPS = [seed, ch10, ch11]

// Content collections of the sample that the fresh-database guard requires to be empty, trashed
// rows included.
export const CONTENT_COLLECTIONS = ['article', 'category', 'tag']
