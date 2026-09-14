// The manual is being rewritten in batches (2026-09-10 rewrite spec). The
// sixteen chapters below predate the rewrite and are replaced batch by batch.
// This is the single, hand-written list of pre-rewrite chapters, shared by
// two consumers: the table-width guard exempts them
// (docs/scripts/check-table-width.mjs — until batch 5 deletes them they would
// fail that check in every locale) and the sidebar groups them into one
// collapsed "legacy" section (docs/.vitepress/sidebar.mts) so they are not
// interleaved with rewritten chapters that reuse the same NN- prefix. It is
// by filename so it applies to both locales at once. Do not add to it: a new
// chapter that does not fit the width rule gets rewritten, not listed here.
// Batch 5 deletes the sixteen files, this module, and both usages together.
export const LEGACY_CHAPTERS = new Set([
  '01-introduction-and-architecture.md',
  '02-getting-started.md',
  '03-configuration-reference.md',
  '04-defining-a-collection.md',
  '05-field-types.md',
  '06-internationalization.md',
  '07-relations.md',
  '08-query-dsl.md',
  '09-rest-api.md',
  '10-graphql-api.md',
  '11-files-and-media.md',
  '12-auth-and-rbac.md',
  '13-revisions-and-soft-delete.md',
  '14-admin-spa-customization.md',
  '15-deployment-operations-testing.md',
  '16-sample-walkthrough.md',
])
