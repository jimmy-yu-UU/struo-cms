# A link write needs read on the target

## Decision

A create or an update whose body names a link needs a read grant on the collection the link points
at, on top of the write grant on the collection written. `RelationTargetReadGuard` refuses it with
`403` and `Read not permitted on '{target}'.` before any id is parsed or looked up. The keys are a
many-to-many array (an empty one included), a many-to-one foreign key (`null` included), a `File`,
`Image` or `Files` field (inside `translations` too), either key of a junction row, and `folderId`
on an upload. A key the body omits is not checked.

For a caller who can read the target, `LinkExistenceCheck` refuses a many-to-one key, a junction key
or a file id that names no row, or a row in the trash, with `400`. Only an id the write newly
assigns is checked.

What is let through:

- On update, a file field (root or inside a locale) and a junction key sent with the value the row
  already stores: an item read returns those values, and a client sends them back. Not a `Hidden`
  field, and not on create.
- A revert leaves out the keys the guard would refuse the caller and applies the rest. It does not
  run the existence check for foreign keys, junction keys and file ids: it replays a snapshot the
  system stored.
- An all-zero GUID is not checked for existence: a non-nullable key uses it for "none".
- Removing a whole locale removes the file links it holds without read on `file`: nothing is linked.

A revision read shows no more links than an item read shows the caller: a many-to-many array or a
foreign key that is not a declared field is left out when the target is not readable, and a
payload-carrying relation is reduced to ids when the junction collection is not readable.

## Why

The read side needs read on a target to expand, filter or sort through a relation. A write that
links a row the caller cannot read lets the caller attach to, detach from and probe a collection
the grant was meant to keep closed. One answer for an existing, a missing and a malformed id keeps
the refusal from telling which ids exist.

The allowance for an unchanged value keeps a read-then-write client working: the value came from a
read the caller was allowed to make, and writing it back links nothing. A `Hidden` field is not
returned by a read, so equality with the stored value would be observable only through the write.

The existence check is not run inside the write transaction and takes no lock, so a row purged
between the check and the insert can still leave a dangling id. That is accepted: the check is for
client mistakes, and the foreign-key columns carry no database constraint to race against.

## Evidence

Measured on SQLite through the HTTP pipeline on 2026-11-26, at the commit before the guard, for a
caller with read and write on the owning collection and no read on the target.

| Write | Answer before the guard |
|---|---|
| many-to-many with an existing id | `200`, linked |
| many-to-many with a missing id | `400` "do not exist" |
| many-to-many `[]` | `200`, every link removed, the ones the caller could not see included |
| many-to-one foreign key, `File`/`Image`/`Files` field, missing id | accepted, the id stored |
| either key of a junction row, missing id | accepted, the id stored |
| upload with `folderId`: missing folder / existing folder | `400` / `201` |

What an item read returns, which decides the allowance for an unchanged value:

| Key | In an item read |
|---|---|
| a foreign key that is only a relation property | no |
| a foreign key that is also a declared field | yes |
| a file field, root or inside a locale | yes |
| the two keys of a junction row | yes |
| a `Hidden` field | no |

A revert, measured on 2026-11-27 (`RevertPurgedLinkTests`, `LocaleRemovalWithoutFileReadTests`):

| Snapshot names | Revert |
|---|---|
| a purged many-to-one target | stored as the snapshot holds it |
| a purged file, in a root field or inside a locale | stored as the snapshot holds it |
| a purged many-to-many target | `400`, the row unchanged |
| no row for a locale the item has, the locale holding a file link | the locale row removed, without read on `file` |

The same ids sent in an ordinary update are refused with `400`.

## Unknowns

- Measured on SQLite only.
- The answer of `origin/main` for a revert that names a purged many-to-many target was read from the
  code, not run.
- `permission.roleId` is a text field, not a relation, and is not covered by either check.
- The existence check matches the rows the database returned ignoring case for a string key. A
  write that names two ids of one string-keyed target differing only in case, of which one exists,
  passes for both on a database that compares strings exactly.

## Referenced from

- `src/Struo.Application/Query/Write/RelationTargetReadGuard.cs`
- `src/Struo.Application/Query/Write/LinkExistenceCheck.cs`
