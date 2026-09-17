# 21. Schema Management and Upgrades

How tables get created, when an existing table gets changed, and how a change to core's own
schema reaches your fork — this chapter covers all three.

## Three layers, three responsibilities

Schema changes split into three layers, each with its own responsibility, its own switch, and
its own environments: table creation, which never touches existing data; schema sync, the layer
most likely to put data at risk; and migration scripts, the most conservative layer and the only
one production should use.

Table creation (CodeFirst): whenever an entity class's table doesn't exist yet, startup creates
it automatically — on every backend, in every environment. It's always on and can't be turned
off.

Schema sync: automatically adds, alters, or even drops columns based on the difference between
an entity class and the existing table. It only takes effect in Development, defaults to off,
and its key and type are in [Chapter 4: Configuration Reference](04-configuration.md). It's also
the only layer that can silently touch existing data — "Where schema sync is dangerous" below
walks through each way that happens.

Migration scripts: apply `.sql` files you write and review yourself. They run against every
backend, not just PostgreSQL, and default to off the same way — leaving `Database:MigrationsPath`
empty disables the runner. This is the only path an existing table should take in production.

Add a property to an existing collection and, by default, nothing happens: the table already
exists, so table creation leaves it alone, and schema sync is off, so the column doesn't grow on
its own. In Development, to make it appear, turn on `Database:AutoSyncSchema` and restart — safe
only against a database that doesn't hold data you actually care about yet.

Production doesn't have that option: the same change needs an `ALTER` script you write yourself,
placed in the migration directory above, deployed with the new version, and applied by the
migration runner at startup. Adding a whole new collection is a different case — that's a table
that doesn't exist yet, so both environments create it on their own and no script is needed.

## Startup order

At startup, six steps always run in the same order:

1. Take a snapshot of the existing tables — used to recognize which tables this startup creates
   for the first time.
2. Table creation — CodeFirst, always runs.
3. Schema sync — only in Development, and only when it's turned on.
4. Apply migration scripts — only when a migrations directory is configured.
5. Schema guard — only in Development.
6. Seed data.

The order isn't arbitrary. Table creation runs first because migration scripts assume the table
they alter already exists — there'd be nothing for `ALTER` to change; schema sync likewise needs a
table to compare against before it can even compute a difference with the entity class.

Schema guard and seeding run last because both depend on the table's final shape: the indexes
schema guard checks for aren't guaranteed to exist until every step that might create or alter a
table has finished, and seeding only fills the tables this startup created for the first time —
running it any earlier would miss columns a migration or schema sync had just added.

If the migration-scripts step fails partway through, the script that was being applied rolls back
along with its tracking entry, right where it stopped — except on MySQL and Oracle, whose DDL
commits implicitly, so whatever already ran can't be undone. Files that had already applied
successfully are not undone and still count. Startup aborts entirely either way; it never carries
on with a script left half-applied.

Only one kind of misconfiguration in the three layers doesn't fail startup: schema sync turned on
outside Development. The whole step is skipped, leaving only a warning that names the current
environment, and startup continues as normal.

## What always happens, and what never happens on its own

The whole mechanism comes down to two guarantees, and neither has an exception across any of the
five backends or any environment.

The first guarantee: whenever an entity class's table doesn't exist, startup always creates it,
and only a table created this way gets seeded. This guarantee describes the code path, not
verified evidence — PostgreSQL is the only backend actually verified against a live database; the
others are supported by design in the type mapping only.

The second guarantee runs the other way: the only two ways an existing table is ever changed
automatically are schema sync explicitly turned on (Development only), or a reviewed script
applied by the migration runner; both are a deliberate choice, and there's no third path that
changes an existing table without anyone knowing.

## Where schema sync is dangerous

> Schema sync only compares the difference between the entity class and the existing table's
> structure — it has no idea what you meant to do. A rename and "drop one column, add another"
> look identical to it. Any schema change that touches existing data has to go through a
> migration.

Every scenario below is what that blindness to intent looks like in practice.

1. **Renaming a column** — sync reads this as "the old property vanished, a new one appeared":
   it issues `DROP COLUMN` on the old column and `ADD COLUMN` on the new one, and the data is
   gone for good. Write a `RENAME COLUMN` migration first, and only change the entity class
   afterward.
2. **Removing a property** — a straight `DROP COLUMN`, and the data goes with it; once you've
   confirmed the column is truly no longer needed, drop it in production with an explicit
   migration.
3. **Narrowing a column's type** — depending on the backend, this either fails outright or
   silently truncates data; the correct approach is three steps — add the new column, backfill
   and verify it, and only then switch over and drop the old one.
4. **Adding `NOT NULL` to a table that already has rows** — the `ALTER` fails outright and
   startup aborts; the three-step migration is add the column nullable, backfill it, and only
   then add the `NOT NULL` constraint.
5. **Adding `UNIQUE` to a column with duplicate values** — the `ALTER` fails the same way and
   startup aborts; the migration deduplicates first (a data operation in its own right), then
   adds the constraint.
6. **Splitting or merging columns** — a structural diff can't express this kind of intent at
   all; the result is always lost data or an empty column. This kind of change is always
   migration-only.
7. **Dropping a column on SQLite** — SQLite's drop-column path is actually implemented; it's
   gated behind SqlSugar's `SqliteCodeFirstEnableDropColumn`, which `SqlSugarClientFactory`
   never turns on. Turned on, an ordinary column gets dropped silently, while a column tied to
   a primary key, an index, a `CHECK`, or a foreign key makes `ALTER TABLE` fail and startup
   abort.
8. **Multiple replicas starting at once** — each replica computes and applies its own diff, so
   several replicas can race to alter the same table at the same time; the same fix as the
   migration runner's lack of locking, covered in "Writing a migration" below.
9. **No way to see the DDL before it runs** — sync computes it live at startup from whatever the
   code looks like at that moment, so there's nothing to preview beforehand. This is the core
   reason schema sync should never be turned on in production.

Items 2 and 7 are each backed by a test:
`PostgresIntegrationTests.Unfiltered_InitTables_drops_a_removed_column_on_postgres` confirms that
a removed column really is dropped on PostgreSQL, and
`DatabaseInitializerTests.Unfiltered_InitTables_does_not_drop_columns_on_Sqlite` confirms the same
change leaves the column untouched on SQLite.

The general rule: whether a backend natively supports some DDL says nothing about whether schema
sync will actually do it on that backend — each backend has to be verified on its own; none of
them can be inferred from another.

## Writing a migration

How to name a file, which SQL to use, why it doesn't need to be idempotent, how applied files are
tracked, and how to write timestamps — `db/migrations/README.md` is the one authoritative version
of all of that, and the full comparison of the three layers is there too. This section covers
only the two things deployment most often trips over.

First, there's no mutual-exclusion lock: several replicas starting at once against the same
migrations directory can all try to apply the same not-yet-applied file simultaneously; table
creation and schema sync carry the same risk. For a staged rollout, let one replica finish
applying first, or run a separate one-off job, before the rest come online.

Second, the directory itself has to exist: `db/migrations/` holds only a `README.md`, no `.sql`
files, so a deployment process that just copies whatever's there won't necessarily carry this
directory into your own build output, though this project's API image does copy it explicitly.
Setting `Database:MigrationsPath` to a directory that doesn't exist fails startup outright; the
message and exit code are in [Chapter 20: Deployment](20-deployment.md).

## The development-time schema guard

`SchemaGuard` runs only in Development, after table creation, schema sync, and migration
scripts. It verifies two things actually exist:

- A composite unique index over `(collectionname, itemid, revisionnumber)` on the `revisions`
  table.
- For every collection with a translation sidecar table, a unique index over `(fk, locale)` on
  that sidecar (the sidecar itself may not exist, but if it does, the index has to).

Missing either one throws an `InvalidOperationException` that says exactly what's missing, naming
the table and the kind of index.

A missing index usually means the table was created before the index was added to the code — an
index is emitted when its table is created. The error message says how to fix it: add a reviewed
migration that creates the index, or, in Development on a schema you don't mind losing, recreate
the schema so table creation emits it again.

It only recognizes PostgreSQL and SQLite; MySQL, SQL Server, and Oracle are skipped entirely, with
nothing verified. It's a development-time fail-fast check for "this change is missing an index
it should have" — not a production safety net, since production never runs it at all.

## The four raw-SQL exceptions in core

The migration scripts this chapter covers are themselves one of the exceptions to the invariant
that all database access goes through SqlSugar, covered in
[Chapter 2: Architecture](02-architecture.md). Here's why each of the four exceptions exists:

- `db/migrations/*.sql` scripts — migrations are exactly where a fork is meant to write its own
  SQL; this layer isn't bound by the rule at all.
- `SchemaGuard`'s read-only catalog queries — an index can't be found by name (different
  backends and different creation paths name them differently), so it has to query a system
  catalog like `pg_indexes`/`sqlite_master` directly; it only runs in Development, and only
  reads, never writes.
- The relation-filter pushdown's subquery wrapper — four assembled string forms, the part of
  translating "filter by a subcollection" query semantics into a SQL subquery that an expression
  tree can't express.
- `OrderByExpressionBuilder`'s `ORDER BY` text — three per-field assembled forms, the same kind
  of sort semantics an expression tree can't express.

Apart from the first, the other three exceptions only assemble strings used in queries — they
never change a table's shape. The first exception is exactly the third layer covered earlier in
this chapter: migration scripts exist precisely to change an existing table's structure.

## Upgrading core across a fork

Core carries no `.sql` scripts at all, and table creation only ever fills in tables that are
missing — it never compares against, or changes, a table that already exists. So a schema change
in core has no way to reach a deployment automatically: a release that changes core's own tables
gives your fork no mechanism that alters your existing tables to match.

Core's schema changes are written up in release notes; acting on them means writing your own
`ALTER` script for whichever backend you actually run, going through the migration path described
earlier. Place the script in the same migration directory and deploy it with the new core version:
startup creates tables first, then applies your script, in the same order as "Startup order"
above.

Turning on schema sync and running it once against a copy of a production database lets you treat
the diff it computes as a hint at roughly which columns this upgrade needs to touch — but only as
a hint, never as a plan to execute directly: every dangerous scenario above still applies to that
diff. The full procedure is in `db/migrations/README.md`.

## What's next

That covers schema management; the next step is what test layers this system has and what CI
runs, in [Chapter 22: Testing and CI](22-testing.md).
