// The run, as one ordered list of groups. Order is the order the shared data set is built and
// changed in: every group that reads a data set unchanged comes before any group that mutates it.
// A file may be fed by several groups (a capture step or its group names the file).
//
//   seed (Guides set, editor)  ->  10  ->  11  ->  14 (quantifier query)  ->  12  ->  13  ->
//   14 (mutation, errors)  ->  15  ->  16  ->  17  ->  Tutorials set  ->  7  ->  8  ->  9
//
// The Tutorials articles are drafts with no publication date, so they are created only after the
// chapters that count or filter the Guides articles; chapter 9 updates S1 and so runs after 7 and 8.
import { ch10 } from './ch10.mjs'
import { ch11 } from './ch11.mjs'
import { ch12 } from './ch12.mjs'
import { ch13 } from './ch13.mjs'
import { ch14First, ch14Rest } from './ch14.mjs'
import { ch15 } from './ch15.mjs'
import { ch16 } from './ch16.mjs'
import { ch17 } from './ch17.mjs'
import { ch7, ch8, ch9 } from './ch7to9.mjs'
import { seed } from './seed.mjs'
import { tutorials } from './tutorials.mjs'

// Coupling: the runner's fresh-database guard (lib/fresh.mjs) logs in as the bootstrap admin under
// the identity named `admin`, and the steps with `as: 'admin'` use that session. A scenario that
// needs another login of its own adds a step for it under a different identity.
export const GROUPS = [seed, ch10, ch11, ch14First, ch12, ch13, ch14Rest, ch15, ch16, ch17, tutorials, ch7, ch8, ch9]

// Content collections of the sample that the fresh-database guard requires to be empty, trashed
// rows included.
export const CONTENT_COLLECTIONS = ['article', 'category', 'tag']
