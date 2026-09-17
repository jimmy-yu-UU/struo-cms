# Coding Conventions (for AI agents)

Conventions that apply across the backend and frontend. Where a convention is enforced by a test or by
the compiler, this document says so and names the mechanism; where it is a convention only, it says
that plainly instead of implying enforcement that does not exist.

## How documentation is written

This repository has two documentation audiences with different needs. Writing for the wrong one is
the most common documentation defect here. Decide which you are writing for before you write, and
apply that audience's rules only.

| Surface | Audience | Rules |
|---|---|---|
| `docs/guide/` (both locales), every `README.md` | A developer meeting StruoCMS for the first time | "For the manual" below |
| `AGENTS.md`, `CLAUDE.md`, `docs/ai/`, code and test comments | An AI agent or maintainer working on this codebase | "For the agent reference set" below |

Both audiences share three rules. First, **write only what the reader needs in order to act.** A fact
earns a paragraph if acting without it would go wrong, and earns zero lines if it only records how the
repository got here.

Second, **load-bearing caveats stay, in every document.** A backend divergence, a security consequence,
an honest "unverified", the reason for a non-obvious decision a future reader would otherwise "fix",
and any limit that would make the reader do the wrong thing are not noise. The test is "would the
reader act differently without this?", never "is this long?". Before deleting a fact that lives only in
the text you are cutting, move it somewhere durable first.

Third, **cite constructs, not lines** — a class, method, branch or distinguishing property, never a
line range; the rule and its one exception are in "Citing code from docs and comments" below, and
`CodeCitationConventionTests` enforces it on the manual chapters and the reference set.

### For the manual

Enforced mechanically where noted; otherwise a reviewer reads the page as a first-time user and
rejects what fails.

- **Voice.** Write the way a colleague explains the system. Short sentences. Use the technical term
  directly — headless, template, fork, junction, sidecar — with no gloss in the other language and
  no bracketed English after a Chinese term. Never introduce the product by what it is *not*; say
  what it is, what it includes, and what the reader does next.
- **Content.** Describe current behavior only. No history, no "this used to", no closed defects, no
  explanation of internal branch order inside a hook. Cite a source file only when the reader must
  open it, at most one per sentence.
- **Layout.** One idea per paragraph, normally four or five lines. Parallel items become a list.
  Each chapter opens with one sentence saying what problem it solves and closes by pointing at the
  next chapter. A chapter stays under about 350 lines; split it when it grows past that.
- **Tables.** At most four columns; each cell fits on one line and holds a value or a phrase, never a
  sentence of explanation. Anything larger becomes a list or a subsection. **Enforced:**
  `docs/scripts/check-table-width.mjs` fails `pnpm -C docs build` on more than 4 columns or any cell
  whose display width exceeds 60 (CJK counts 2). There is no exemption marker; rewrite the table. It
  finds tables by their delimiter row, as the renderer does, so neither missing outer pipes nor list
  indentation hides a table from it.
- **Transcripts.** Keep a command or HTTP transcript only when it shows something prose cannot, and
  capture it from a real run. Never hand-edit one.
- **Locales.** Traditional Chinese is the master text. The English chapter follows the Chinese
  chapter's section structure and content, written in natural English, not translated sentence by
  sentence. Both locales use the same filename.

### For the agent reference set

- **Precision over voice.** Density is fine; ambiguity is not. Every rule is stated once, in one
  home, and linked from anywhere else it is relevant. Restating a rule elsewhere is allowed only when
  a reader of *that* document would otherwise act wrongly — and then restate the rule, not its
  rationale.
- **Consequences, not preferences.** Every constraint says what breaks when it is violated, and a
  rationale that names what breaks stays. A note that only records that someone once decided against
  something protects no code; cut it.
- **Evidence goes to the code.** Measurement runs, ruled-out hypotheses and residual unknowns belong
  in the doc comment of the class they explain, with a one-line summary and pointer here.
  `PgTestConnectionString` is the worked example.
- **Structure of `AGENTS.md`** stays: repo map, hard constraints, invariants, task playbooks,
  verification, prohibitions.

### Anti-patterns this repository has actually had (both audiences)

| Anti-pattern | Instead |
|---|---|
| Change narrative: "this used to be X, now Y" | State the current behavior |
| Investigation journal in a chapter | Doc comment of the class, plus a pointer |
| Same rule in N places | One home, links elsewhere |
| A summary that re-explains its own section | A checklist of steps |
| A note justifying a feature *not* built | Cut it |

The same applies to tests: a test asserting the **absence** of unimplemented behavior protects
nothing and constrains whoever later implements the feature. A test asserting a *negative* behavior
of code that exists ("never reorders rows client-side") guards a real path and stays.

## Citing code from docs and comments

When documentation or a comment points **into this repository's own code**, cite the construct — a
class, method, branch, or a distinguishing property — rather than a line range, because line ranges
rot silently the first time someone inserts above them and nothing in CI catches it.
The deliberate exception is a citation into **pinned upstream SqlSugar source**, as in
`src/Struo.Infrastructure/Persistence/ColumnTypeMap.cs`: that source is version-pinned and cannot
shift underneath us, so a line range there stays valid.

Enforced by `CodeCitationConventionTests`
(`tests/Struo.Tests/Documentation/CodeCitationConventionTests.cs`), mechanically, for two of the
four shapes citations take. A citation that names a file extension (e.g. `SomeFile.cs:123`,
citation-guard:allow) is a violation only if the cited file resolves to a real path under `src/`,
`tests/`, `frontend/src/`, `schema/`, `db/`, `docs/` or `samples/` (or is `AGENTS.md`/`CLAUDE.md`
itself) — a bare filename matches by basename anywhere under those roots, a directory-prefixed one
(as most upstream citations here are written) must match a real path suffix, and there is
deliberately no upstream-filename allowlist, which would go stale. A citation with no file extension
at all — a dotted `Construct.Member` name immediately followed by a hyphenated range — names no file
to check, so it is **always** a violation with no escape hatch; write upstream citations in the
extension-anchored form, never this one. A bare trailing number with no filename (`` `:145` ``) or a
prose reference ("line 74") is not reliably distinguishable from a port or a time and is not covered;
a reviewer still has to catch those by hand.

## Mustache syntax in the manual

The documentation site compiles every chapter into a Vue component, so Vue's template compiler sees the
rendered text — including the contents of inline `` `code` `` spans. A literal `{{ ... }}` there is
parsed as an expression, and neither outcome is a build failure:

- **`{{ something.property }}` empties the whole chapter.** `vitepress build` **exits 0** and prints
  `build complete`, while that chapter is written out with its navigation and page frame intact and its
  body gone. It also drops out of the search index, which is built from rendered content, and the page
  count stays right. Only stderr says anything, and it names neither the markdown file nor the cause:

  ```
  TypeError: Cannot read properties of undefined (reading 'label')
  ```

  `pnpm build` catches this one, because `docs/scripts/check-rendered-chapters.mjs` runs after the build
  and fails when a rendered chapter has no heading.

- **`{{ something }}` — a bare identifier — empties only that sentence.** It interpolates to the empty
  string, nothing throws, and `vitepress build` is green — the rendered-output check above sees a heading
  and passes. `pnpm build` still catches it: `check-rendered-chapters.mjs` also scans every chapter
  *source* for a bare `{{` outside a fenced code block and unwrapped by `<span v-pre>`, which needs no
  build to run and catches this form specifically.

Wrap the span:

```md
<span v-pre>`<span class="readonly-relation">{{ relation.label }}</span>`</span>
```

Fenced code blocks need nothing — VitePress applies `v-pre` to them already.

A related constraint from the same config (`docs/.vitepress/config.mts`'s `srcDir: 'guide'` plus
`ignoreDeadLinks: false`): a chapter may only markdown-link to another chapter, since anything outside
`docs/guide/` is outside the site entirely. A link like `[AGENTS.md](../../AGENTS.md)` fails the build
with a dead-link error. Reference a repo path in a bare code span instead — every chapter already does.

## Naming

- **C# types and members**: PascalCase, standard .NET convention throughout `src/` and `tests/`.
  Interfaces are prefixed `I` (`IItemRepository`, `IMetadataProvider`, ...).
- **Database tables and columns**: lower-case, plural, snake_case (`languages`, `file_translations`,
  `media_folders`, `user_roles`) — every framework and sample entity follows this via `[SugarTable]`
  (`docs/guide/en/05-collections.md`). Columns follow SqlSugar's default lower-casing of the
  CLR property name.
- **Collection/field JSON names**: camelCase on the wire (`fileName`, `createdAt`) — the outbound
  `JsonSerializerOptions` are `JsonSerializerDefaults.Web` throughout (see `EnvelopeJsonOptionsHolder`,
  `src/Struo.Api/Http/EnvelopeJsonOptionsHolder.cs`, and the MVC `JsonOptions` configured in
  `Program.cs`), and `MetadataScanner` derives each field's camelCase name from the CLR property name.
- **Migration files**: `NNN-short-kebab-description.sql`, zero-padded, one contiguous series
  (`db/migrations/README.md`) — the numeric prefix is the apply order via ordinal filename sort, so it
  must be monotonic and gap-free; the next number is always the current highest **+ 1**.
- **Frontend field interfaces**: camelCase string literals (`richText`, `multiSelect`, `checkboxGroup`)
  mirroring the backend `FieldInterface` enum member names — `frontend/src/lib/fieldTypes/types.ts`'s
  own comment states this must be kept in sync by hand; nothing generates one from the other.
- **Test class/file names**: one test class per source concept, named `<Subject>Tests.cs`
  (`ItemsEndpointTests.cs`, `MetadataScannerTests.cs`, `TemplateInvariantsTests.cs`) or
  `<subject>.test.ts` for frontend unit tests, co-located next to the source file it covers
  (`frontend/src/lib/buildItemPayload.ts` / `buildItemPayload.test.ts`).
- **M2M junction payload on read**: a fixed, reserved key, `_junction`, attached by `RelationExpander`
  to each `deep`-expanded target row of a many-to-many relation whose junction carries payload (below)
  — not derived from the junction collection's own name, and not present at all on a payload-free
  relation. GraphQL's generated type/field names around the same feature (`<Parent><Rel>Link`,
  `<Parent><Rel>Junction`, `<Parent><Rel>LinkInput`, `<rel>Links`) are the ordinary
  `Pascal`/`Camel` mechanical naming above, applied to the relation's owning collection and relation
  name (`SchemaTypeMapper.LinkTypeName`/`JunctionTypeName`/`LinkInputName`/`LinksFieldName`).
- **`FormModel.relations` value shapes**: three possible shapes per relation name — a bare `string` (or
  `null`, `frontend/src/lib/parseItemToForm.ts`'s `nested?.id ?? null`) for a many-to-one, a `string[]`
  for a `tagSelect` relation whose junction carries no *visible* payload and no `SortField`, or a
  `RelationLink[]` (`{ id, junction }`, `frontend/src/types/itemForm.ts`) for a `tagSelect` relation
  with either. `usesLinksEditor` (`frontend/src/lib/junctionLinks.ts`) is the single point that decides
  which shape a given relation uses, so `RelationInput`, `buildItemPayload`, and `JunctionLinksEditor`
  never diverge on the question. `buildItemPayload` sends `{id, ...payload}` object elements — rather
  than bare ids — only when three things all hold: the caller passed a `resolveCollection`, the
  relation has at least one visible payload field, and `canWriteJunction` (also `junctionLinks.ts`)
  returns true for it.

## Column type mapping

When a property needs a DDL column type other than SqlSugar's default C#-type mapping, prefer
`[ColumnShape]` (`src/Struo.Infrastructure/Persistence/ColumnShape.cs`) over a literal
`[SugarColumn(ColumnDataType = "...")]`: the shape is a dialect-neutral enum member (`LongText`,
`TimestampWithTimeZone`) that `ColumnTypeMap.For` (`src/Struo.Infrastructure/Persistence/
ColumnTypeMap.cs`) resolves to the correct per-backend literal inside `SqlSugarClientFactory`'s
`EntityService` hook, so the same property is *designed* to work unchanged on PostgreSQL, MySQL, SQL
Server, Oracle, and SQLite — but only the PostgreSQL and SQLite literals are exercised by a live
instance; the MySQL/SQL Server/Oracle literals are chosen to be syntactically valid and are not
claimed to be verified against a live instance of those three (`ColumnTypeMap.cs`'s own class doc has
the full evidence trail, including a source-read-only conclusion about a parenthesised-literal edge
case on SQL Server/MySQL). A fork that only ever runs one backend is free to write
`[SugarColumn(ColumnDataType = "...")]` directly instead — that convention is respected too, just at
lower precedence.

**Precedence, if a property carries both**: `[ColumnShape]` wins, silently — the hook resolves the
shape and returns before the explicit `ColumnDataType` is ever consulted
(`SqlSugarClientFactory`'s `EntityService` hook, the whole `[ColumnShape]` branch: the shape is read
at the top and the branch returns at its end). There is no warning for the conflict; a fork that
wants its own vendor literal to win on a shaped property must remove `[ColumnShape]` from it. Pinned by
`ColumnTypeMapTests.ColumnShape_wins_over_an_explicitly_declared_ColumnDataType`
(`tests/Struo.Tests/Persistence/ColumnTypeMapTests.cs`).

**A JSON-column `[CmsField]` on the same property is refused, not resolved**: the hook throws an
`InvalidOperationException` naming the property and the offending interface when `[ColumnShape]`
co-occurs with a `JsonColumnInterfaces` member
(`MultiSelect`/`CheckboxGroup`/`Tags`/`KeyValue`/`Files`/`Repeater`). The shape branch's early return
outranks the later JSON branch, which sets both `IsJson = true` and a widened `DataType`; both
branches resolve a JSON-column interface to `LongText`, so with the shape declared `LongText` — the
only sensible choice here — the combination's only effect was silently dropping `IsJson`, and without
it SqlSugar never serializes the collection and the column takes CodeFirst's unset length —
`varchar(1)` on PostgreSQL, which rejects every real value with 22001. Remove `[ColumnShape]` from
such a property; the JSON mapping already applies `LongText`. A **content-bearing** interface
(`RichText`/`Textarea`/`Markdown`/`Code`/`Json`) is unaffected and stays legal — both paths compute
`LongText`, so nothing is lost. Pinned by
`ColumnTypeMapTests.ColumnShape_combined_with_a_JSON_column_CmsField_is_refused` and
`..._combined_with_a_content_bearing_CmsField_is_still_allowed`.

Core itself must never write a vendor type literal outside `ColumnTypeMap.cs` — that file is the one
place in `src/` a string like `"timestamptz"` or `"longtext"` may appear. An empty MySQL/SQL Server/
Oracle database fails `InitTables` outright on a type name that only exists on PostgreSQL, so any
`[SugarColumn(ColumnDataType = ...)]` added to a framework entity must go through `[ColumnShape]`
instead.

## Translation sidecar unique index

A translation sidecar's `(fk, locale)` UNIQUE index — the constraint that keeps at most one translation
row per parent per locale — is **derived, not declared**. Nothing on `FileTranslation` or
`Struo.Sample.Blog.ArticleTranslation` names the pair; the same `EntityService` hook that widens `IsJson`
columns reads each sidecar's `[CmsTranslations(typeof(T))]` metadata via `TranslationSidecarIndexPolicy`
(`src/Struo.Infrastructure/Persistence/TranslationSidecarIndexPolicy.cs`) and stamps the resolved group
name onto the foreign-key and locale columns' `EntityColumnInfo.UIndexGroupNameList` before `InitTables` reads
it, so CodeFirst emits the composite `UNIQUE` on table creation with no attribute on the entity at all.

`SqlSugarClientFactory.Create` takes the policy as an optional third parameter defaulting to
`TranslationSidecarIndexPolicy.None` (no sidecars, no uniques). `AddStruoInfrastructure`
(`src/Struo.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`) registers the real policy
as a singleton built from `IMetadataProvider.GetCollections()` and resolves it into the `ISqlSugarClient`
factory registration, so any host going through DI gets the derivation for free. A fork that constructs
`SqlSugarClientFactory.Create` itself — bypassing `AddStruoInfrastructure`'s registration, or calling it
outside DI entirely — must pass `TranslationSidecarIndexPolicy.FromMetadata(metadata.GetCollections())`
explicitly; omitting it silently falls back to `None` and every sidecar table comes up with no unique
index at all, not an error at that point. `SchemaGuard.AssertCriticalConstraintsAsync`
(`src/Struo.Infrastructure/Persistence/SchemaGuard.cs`), run at Development startup, is what actually
catches the gap: it re-checks each sidecar table for a UNIQUE index covering its `(fk, locale)` columns
by uniqueness and column coverage, not by name, and throws with an actionable message if one is missing.

## Many-to-many junction payload

A relation's payload field list — which of a junction collection's `[CmsField]`s (beyond its two
foreign keys and the relation's `SortField`) are exposed as the link's own data — is computed in
exactly one place: `MetadataScanner.ResolveJunctionPayloadFields`
(`src/Struo.Infrastructure/Metadata/MetadataScanner.cs`), which excludes the two FKs and the sort
column and keeps only the fields that survive the `!IsSystem && !ReadOnly` filter, writing the
surviving names into `RelationMetadata.JunctionPayloadFields`. `RelationshipGraph.JunctionPayloadOf`
(`src/Struo.Infrastructure/Metadata/RelationshipGraph.cs`) never re-derives that list; it only resolves
those already-chosen names into CLR properties (plus each field's `Hidden` flag) as
`M2MDescriptor.JunctionPayload`. Every downstream consumer — the REST mixed-array write binder
(`ItemWriteSideSync.SyncM2MAsync`), the diff-and-patch sync (`ManyToManySync`), the `_junction` read
projection (`RelationExpander`), revision snapshots
(`RevisionSnapshotBuilder`/`RevisionSnapshotRedactor`), and the GraphQL `<rel>Links` surface
(`CollectionSchemaBuilder`) — reads `M2MDescriptor.JunctionPayload` through `JunctionPayloadOf` rather
than re-deriving which fields count as payload; a field only reaches any of those surfaces if
`MetadataScanner.ResolveJunctionPayloadFields` already excluded it from "structural" (the two FKs, and
the sort column when the relation declares one) and it survived the `!IsSystem && !ReadOnly` filter
applied there.

A many-to-many relation's write-side array element (REST body key, or GraphQL `<rel>Links` entry after
`MutationResolvers.FoldLinks` folds it into the REST shape) is one of two forms: a **bare id** (link
this target, leave its junction row's payload untouched) or an **object** `{ id, ...payload }` (link
this target and merge the named payload fields into its junction row).
`docs/guide/en/08-relations.md`, "Junction entities and payloads", states the full precedence rule
for when the same id appears more than once in one array (an object always outranks a bare id;
between two objects the later one wins; order of first appearance is the sort order) and is the
canonical source for that rule; `docs/guide/en/12-rest-conventions.md`, "The many-to-many write
shape", shows the same value written into a `PUT` body. This section only names where the code that
enforces it lives (`ItemWriteSideSync.SyncM2MAsync`).

## File organization

Both `Struo.Application` and `Struo.Infrastructure` are organized **by feature area**, not by technical
role, though the two projects don't carry identical folder sets — each only has the folders its own
concerns need. `Struo.Application`: `Abstractions/`, `Configuration/`, `Files/`, `Localization/`,
`Metadata/`, `Query/` (with `Query/Read/` and `Query/Write/` sub-folders for the read/write split, plus
`Query/Write/Validators/`), `Revisions/`, `Security/`, `Settings/`. `Struo.Infrastructure`:
`DependencyInjection/`, `Files/`, `Health/`, `Identity/`, `Localization/`, `Metadata/`, `Persistence/`,
`Query/`, `Revisions/`, `Security/`, `Settings/` — it additionally owns `Identity/` (the concrete user/
role/permission entities and SqlSugar wiring), `DependencyInjection/` (the general-purpose `AddStruoXxx`
extension methods — `AddStruoData`, `AddStruoFiles`, `AddStruoMetadata` (two overloads),
`AddStruoInfrastructure`; the four host-specific ones — `AddStruoAuth`, `AddStruoCors`, `AddStruoOidc`,
`AddStruoGraphQl` — live in `src/Struo.Api` instead), `Health/`, and `Persistence/` (SqlSugar
client/migration plumbing), none of which `Struo.Application` has any need for. `Struo.Api` mirrors this: `Controllers/`, `GraphQl/`,
`Http/`, `Auth/`. `tests/Struo.Tests` mirrors the same feature folders (`Metadata/`, `Query/`, `Api/`,
`Files/`, `Identity/`, ...) so a change to one feature's production code has an obvious, adjacent home
for its test. New code should follow the same pattern: add to (or create) a feature-named folder rather
than a generic `Services/`/`Helpers/`/`Utils/` bucket, and keep files small and single-purpose — the
codebase's existing files split by responsibility rather than by layer role (e.g. `ItemDeserializer`,
`ItemProjector`, `QueryValidator`, `FieldValidatorRegistry` are separate files even though they all
serve `ItemService`).

## Error handling and the response envelope

Every REST response — success or error — is wrapped in one of two shapes by `EnvelopeResultFilter`
(`src/Struo.Api/Http/EnvelopeResultFilter.cs`), an `IAlwaysRunResultFilter`:
- Success: `{ "success": true, "data": ..., "meta"?: { "total", "limit", "offset" } }` — `meta` is
  omitted entirely (never emitted as `null`) unless the action returned a paginated `PagedResult`.
- Error: `{ "success": false, "error": { "code", "message", "details"?: [{ "field", "message" }] } }` —
  `details` is present only for the `VALIDATION` code.

`Envelope.Success`/`Envelope.Error` (`src/Struo.Api/Http/Envelope.cs`) are the only helpers that should
construct these shapes; a controller action that needs a bare error response uses
`ApiResults.Fail(status, code, message)` (`src/Struo.Api/Http/ApiResults.cs`) rather than hand-building
an `ObjectResult`, so `EnvelopeResultFilter`'s idempotency check (already-an-envelope → left untouched)
keeps working. Domain exceptions are mapped to the stable `ErrorCodes` set
(`src/Struo.Api/Http/ErrorCodes.cs`) by `DomainErrorMap`
(`src/Struo.Api/Http/DomainErrorMap.cs`) — the single source of the exception→code mapping, shared
verbatim by REST's `StruoExceptionHandler` and GraphQL's `StruoErrorFilter` so the two protocols cannot
drift apart. A new domain exception type that should surface a client-safe message needs a new arm in
`DomainErrorMap.Map` (and, if it needs a REST status other than the fallback 500, in
`DomainErrorMap.StatusFor`); anything left unmapped collapses to `INTERNAL_SERVER_ERROR` with a masked
generic message — the real exception is logged server-side, never leaked to the response. See
`docs/guide/en/12-rest-conventions.md`.

`SearchUnavailableException` (`src/Struo.Domain/Query/SearchUnavailableException.cs`) is the one
exception `DomainErrorMap.Map` does **not** surface verbatim: it maps to `ErrorCodes.SearchUnavailable`
(`SEARCH_UNAVAILABLE`/503) with the fixed client-facing message
`DomainErrorMap.SearchUnavailableMessage` ("Search is temporarily unavailable.") rather than the
exception's own `Message`, since a fork's `ISearchProvider` may put an internal host or endpoint into
that message — it is logged server-side at `Warning` (both `StruoExceptionHandler` and GraphQL's
`StruoErrorFilter`) and never reaches the response.

A registered `IItemChangeListener` (`src/Struo.Application/Changes/IItemChangeListener.cs`) that throws
follows a different rule from every exception above: it never reaches `DomainErrorMap` or the envelope
at all. `ItemChangeNotifier` (`src/Struo.Infrastructure/Changes/ItemChangeNotifier.cs`) catches it
directly, logs it at `Error`, and moves on to the next listener — the write already committed before
notification ran, so the response the caller receives is exactly what it would have been with no
listener registered (or a listener that succeeded). There is no error code for a listener failure and
none is ever surfaced client-side.

## Input validation at boundaries

- **Query DSL** (filter/sort/search/fields/deep/facets/aggregate paths): validated and whitelisted by
  `QueryValidator` (`src/Struo.Application/Query/QueryValidator.cs`) against the scanned `CollectionMetadata` before any
  SQL is built — an unknown field or relation path is rejected with `QueryException` /
  `BAD_USER_INPUT`, never passed through to the ORM. `QueryValidator` also enforces RBAC, not just
  metadata shape: `DenyUnreadableHops` walks a dotted filter/sort path hop by hop and throws
  `PermissionDeniedException` on the first collection the caller cannot read (a `_junction` segment's
  read grant is checked against the junction collection at the same point). The relation-quantifier
  tokens `_some`/`_none`/`_junction` (`some`/`none`/`junction` in GraphQL) are reserved exactly like
  `_and`/`_or` — `FilterReservedTokens.All`
  (`src/Struo.Application/Query/FilterReservedTokens.cs`) is the single source of that set, and a
  field or relation named one of them fails fast at startup. Every relation filter — a dotted path, a
  `_some`/`_none` quantifier, or a translatable-field condition reached at any hop — is pushed down
  into a nested SQL subquery by `FilterTranslator`
  (`src/Struo.Infrastructure/Query/FilterTranslator.cs`, `FilterTranslator.Subquery.cs`) rather than
  resolved to an in-memory id set first; that subquery never clears the target collection's
  soft-delete filter, regardless of the outer request's own `?deleted=`. This translator hand-assembles
  exactly four SQL string forms and nothing else — `<col> IN (<sql>)`, `<col> NOT IN (<sql>)`,
  `(<col> IS NULL OR <col> NOT IN (<sql>))` (`SubQueryConditional.cs`), and the OR-merge of two or more
  of those, `(<sql1> OR <sql2> OR …)` (`OrOfSubqueriesConditional.cs`) — every other fragment of SQL
  text comes from SqlSugar's own `ToSql()`, never a hand-built dialect-specific string. Sort is the
  other place SqlSugar's typed surface runs out: `OrderByExpressionBuilder`
  (`src/Struo.Infrastructure/Query/OrderByExpressionBuilder.cs`) is the sole source of the string
  passed to the one `queryable.OrderBy(string)` call, in `SqlSugarItemRepository.RunQueryAsync`, and it
  assembles three per-field forms — a plain column (`<col> ASC|DESC`), a to-one relation-path sort as
  a correlated subquery with one JOIN per hop (`RelationOrderExpr`), and a translatable-field sort as
  a correlated subquery against the translation sidecar with the query locale embedded as an escaped
  string literal (`TranslatableOrderExpr`; the locale is either a request locale already validated by
  `ItemService.ValidateLocale`, or the configured default code whose format is guarded on write by
  `ValidateLanguageCodeIfNeeded` — the quote-doubling on the literal at this sink remains either way)
  — plus the no-client-sort default clause (`<created> DESC, <id> ASC`, or `<id> ASC` alone) and the
  `, <id> ASC` tiebreak/comma-join that wrap every sort. Every column name across all of it comes from
  `db.EntityMaintenance.GetDbColumnName`/`GetTableName`. This is the fourth of `AGENTS.md`'s four
  raw-SQL exceptions. Every filter value `ConditionalModelTranslator` renders into a `ConditionalModel`
  (`ToFieldValue`) is formatted with `CultureInfo.InvariantCulture`, and `Program.cs` sets the host
  process's own default culture to invariant at startup — needed because SqlSugar re-parses that same
  rendered value back with `CultureInfo.CurrentCulture` (keyed off `CSharpTypeName`), so the two sides
  must agree or a decimal/DateTime literal silently comes out wrong under a non-invariant culture; a
  headless JSON API has no culture-formatted output of its own to lose by running invariant — see
  `docs/guide/en/10-query-basics.md`'s "Which fields can be filtered and sorted, and what happens
  when it goes wrong" for whitelisting and unknown paths, and `docs/guide/en/11-query-advanced.md`'s
  "Deep expansion `deep=`" for the depth cap. `facets=`/`aggregate[<op>]=` (chapter 11's "Facet
  counting" and "Aggregates")
  are validated the same way, by the same `QueryValidator`, with their own whitelist: a facet path is
  at most one relation hop, never a quantifier or `_junction` segment, its own/leaf field's interface
  must be in `FacetPathResolver.Facetable` (excludes long-form text, every multi-value interface,
  structured payloads, and `Hidden`), and a relation hop's target collection needs its own read grant,
  checked before the path's shape is resolved — same permission-first ordering `DenyUnreadableHops`
  uses for a dotted filter path. `FacetFilterPruner.Prune`
  (`src/Struo.Application/Query/FacetFilterPruner.cs`) then removes every filter condition on a
  facet's own field/relation family (the FK column, any `<relation>.`-dotted path, and a
  `_some`/`_none` predicate against that relation all count as one family) before that facet is
  counted — a pure function over the already-validated `FilterNode` tree, never touching `search` or
  the separately-validated `aggregate` spec. `ValidateAggregate` checks an op against a fixed
  interface-compatibility table (`count` on any own field; `sum`/`avg` only `Number`/`Slider`/`Rating`;
  `min`/`max` those plus `Date`/`DateTime`) before any aggregate SQL runs. `FacetQueries`
  (`src/Struo.Infrastructure/Query/FacetQueries.cs`, `FacetQueries.Leaf.cs`) and `AggregateQueries`
  (`src/Struo.Infrastructure/Query/AggregateQueries.cs`) hold to the same typed-API-only rule as
  `FilterTranslator` above — `GroupBy`/`OrderBy`/`Select` are always given a runtime-built
  `Expression<Func<T, …>>` lambda, never a string, and the one subquery each needs (a to-many facet's
  filtered-root-ids side query) goes through the same `SubQueryConditional.Wrap` this section's raw-SQL
  exception already covers, not a new one. A registered `ISearchProvider`'s returned candidate ids are a
  **trust boundary distinct from user input**: `SearchCandidateResolver`
  (`src/Struo.Application/Search/SearchCandidateResolver.cs`) parses each one to the collection's
  primary-key CLR type (only `Guid` or an integer type is accepted — anything else is refused) and caps
  the count at `Query:MaxSearchCandidates`. Both a rejected PK type/unparsable id and an over-cap count
  throw `InvalidOperationException` (→ `INTERNAL_SERVER_ERROR`/500) rather than `QueryException` (→
  `BAD_USER_INPUT`/400) — the violation is the fork's provider misbehaving, not something the caller
  sent, so it must not be reported as a client mistake. See
  `docs/guide/en/18-extension-points.md`'s "The search provider".
- **Write bodies**: `ItemDeserializer` (`src/Struo.Application/Query/Write/ItemDeserializer.cs`) parses
  the request JSON against the collection's metadata (unknown/`ReadOnly`/system fields are stripped,
  not silently trusted) and sanitizes non-translatable `RichText` values via `RichTextCleaner` (a
  wrapper around `IHtmlSanitizer`, `src/Struo.Application/Query/Write/RichTextCleaner.cs`) before
  required-field checks run; translatable `RichText` values go through the same `RichTextCleaner`
  separately, per locale, in `ItemWriteSideSync.SyncTranslationsAsync`
  (`src/Struo.Application/Query/Write/ItemWriteSideSync.cs`). `FieldValidatorRegistry`'s
  per-`FieldInterface` validators
  (`src/Struo.Application/Query/Write/Validators/`) enforce structural constraints for `MultiSelect`/
  `CheckboxGroup`/`Tags`/`KeyValue`/`Files`/`Repeater` fields, in that fixed phase order.
  `Required` is enforced for non-translatable fields at this layer.
- **Configuration**: bound with the `IOptions` pattern and validated with `ValidateOnStart` —
  `Database`, `Struo:Files`, `Oidc`, and `Query` options all fail fast at startup (before the app
  accepts a single request) rather than surfacing as a confusing first-request failure
  (`tests/Struo.Tests/DependencyInjection/OptionsValidationTests.cs` exercises this against a real
  generic host). `FileStorageOptionsValidator`
  (`src/Struo.Infrastructure/DependencyInjection/FileStorageServiceCollectionExtensions.cs`) is an
  example of wrapping a plain `Validate()` method into that pipeline.
- **RBAC**: enforced inside `ItemService` (`src/Struo.Application/Query/ItemService.cs`), which calls
  `CanRead`/`CanWrite`/`CanDelete` on the injected `IPermissionService` (`RbacPermissionService`, which
  reads `ICurrentPermissions.Current`), throwing `PermissionDeniedException` on denial — not by a
  per-controller-action attribute for generic collection CRUD. `[CmsCollection(AdminOnly =
  true)]` collections additionally require a super-admin for any write, checked the same way. Reads
  that traverse into a related collection are checked there too, not only on the root: besides
  `QueryValidator.DenyUnreadableHops` above, `DeepExpansionCoordinator.PruneUnreadable`
  (`src/Struo.Application/Query/Read/DeepExpansionCoordinator.cs`) silently omits an unreadable `deep=`
  relation instead of failing the read, and `TranslationOverlay`
  (`src/Struo.Application/Query/Read/TranslationOverlay.cs`) gates translatable Image/File resolution
  on `CanRead` for the file collection. An anonymous-read deployment needs a
  `Rbac:PublicReadCollections` entry for every collection a public filter or `deep=` traverses, not
  just the root. See `docs/guide/en/17-roles-and-permissions.md`.

## Configuration over hardcoding

Every deployment-tunable value is bound through the `IOptions<T>` pattern from `appsettings.json` /
environment-variable overrides (`Section__Key`), never hardcoded in source — `DatabaseOptions`,
`FileStorageOptions`, `OidcOptions`, `StruoQueryOptions`, `RateLimiting:Login`, `Serilog:*`, and so on
(`docs/guide/en/04-configuration.md` is the full reference). A relative filesystem path in
configuration must be resolved against the correct root explicitly —
`Struo:Files:ImageTransform:CachePath` is resolved against `IHostEnvironment.ContentRootPath`
(`FileStorageServiceCollectionExtensions.cs`), which is the pattern to copy; `Database:MigrationsPath`
by contrast is passed straight to `Directory.Exists` with no content-root resolution of its own, so it
**must** be given as an absolute path in Production
(`src/Struo.Infrastructure/Persistence/MigrationRunner.cs`) — see
`docs/guide/en/20-deployment.md`, "Production checklist". New tunables should follow the
`ImageTransform:CachePath` pattern (explicit content-root resolution), not the `MigrationsPath` one.

## Immutability

Domain and query model types are C# `record`s with `init`-only properties
(`src/Struo.Domain/Metadata/Models/CollectionMetadata.cs` and siblings; `QueryModel`,
`src/Struo.Domain/Query/`). Updating a value produces a new instance via `with` rather than mutating in
place — for example `QueryValidator.cs`'s `return q with { Limit = limit, Offset = offset };`. New code
in `Struo.Domain`/`Struo.Application` should follow the same pattern: prefer `record`/`sealed record`
with `init` properties and non-destructive `with` updates over mutable classes with setters, especially
for anything that flows through the metadata cache or the query pipeline (both are shared, longer-lived
state where an accidental in-place mutation would be visible to every subsequent caller).

## Overlay stacking (frontend)

Every component under `frontend/src/components/ui/` shares one `z-50` for its floating layer (Popover,
Select, Dialog, Sheet, dropdown, tooltip, combobox, alert-dialog, ...), and nothing in `ui/` goes higher.
`ConfirmHost` (`frontend/src/components/shell/ConfirmHost.vue`) is the one exception: it is a
Pinia-backed singleton that can open while another vendored overlay is already on screen, and at equal
`z-50` a fixed-position element's stacking falls to DOM order rather than intent, so its
`AlertDialogContent` is raised to `z-[60]`. A new floating layer must stay under that ceiling —
`z-[60]` is reserved for this one singleton, not a scale to build on.

## Tests per layer

- **Backend** (`tests/Struo.Tests`, xUnit, run with `dotnet test`): most tests build a fresh SQLite
  temp-file database per test (`Support/SqliteTestDatabase.cs`, deleted on dispose). An opt-in
  live-PostgreSQL suite (`PostgresIntegrationTests`) exists specifically to catch "SQLite-green ≠
  Postgres-correct" bugs; it activates only when `Testing:PostgresConnection` is configured — resolved
  from the `STRUO_TEST_PG_CONNECTION` environment variable first, falling back to the
  `Testing:PostgresConnection` key in `src/Struo.Api/appsettings.json`/`appsettings.Development.json` if
  the env var is unset — and is otherwise a no-op pass; it also refuses to run against any database
  whose name doesn't contain `test`. Integration-style tests for the HTTP
  surface live under `tests/Struo.Tests/Api/` using a `WebApplicationFactory`-based fixture
  (`Support/ApiFactory.cs`). `tests/Struo.Tests/Template/TemplateInvariantsTests.cs` is the one suite
  that guards template-shape invariants (no `samples/*` reference from `Struo.Api`, empty shipped
  `ContentAssemblies`) rather than exercising `AddStruoMetadata` at runtime.
- **Frontend unit** (`pnpm test`, Vitest, `jsdom` environment): `*.test.ts` files sit next to the
  source file they cover (e.g. `frontend/src/lib/buildItemPayload.ts` /
  `frontend/src/lib/buildItemPayload.test.ts`).
- **Contract** (`schema/core-collections.json` and `schema/interfaces.json` plus
  `tests/Struo.Tests/Api/CoreSchemaSnapshotTests.cs` and `frontend/tests/schemaContract.test.ts`):
  committed snapshots of the core collections' `GET /api/schema` wire shape and of every declared
  `FieldInterface`/`RelationInterface` member, checked from both ends. It exists because the admin SPA
  mirrors the backend DTOs and enums by hand, and drift between them is silent to every other gate — an
  unknown `FieldInterface` falls back to a read-only renderer instead of erroring, a field whose
  interface has no list-column formatter just disappears from the list view, and an unmapped
  `RelationInterface` falls back to `'readonly'`. The enum snapshot is what makes the interface half
  unconditional: a new member is caught when it is declared, not only once some core collection uses it.
  Unlike E2E, this layer **is** run by CI:
  nothing about it is live or external — the backend half exercises `GET /api/schema` through the same
  in-process `WebApplicationFactory`/SQLite fixture other API tests use, not a running server or a real
  database — so both halves ride inside the existing `dotnet test`/`pnpm test` commands. See
  `schema/README.md` for the full contract and the regeneration command. `schemaContract.test.ts`
  deliberately departs from the co-location convention the Frontend unit bullet above states: it lives
  in `frontend/tests/`, not next to a source file, and runs under Vitest's `node` environment rather
  than `jsdom` (a `// @vitest-environment node` pragma, needed to read the snapshot file from disk
  without Vite's dev-server URL rewriting getting in the way).
- **E2E** (Playwright, `frontend/playwright.config.ts`): two projects — `core` (`pnpm e2e`) runs
  framework-only specs under `frontend/e2e/` (excluding `e2e/sample/**`) against the shipped template
  with zero content collections; `sample` (`pnpm e2e:sample`) runs `e2e/sample/**` and needs the Blog
  sample opted in first. Neither is run by CI (`.github/workflows/ci.yml` runs the five standing gates
  — `dotnet build` + `dotnet test`, `pnpm test` + `pnpm build` from `frontend/`, and `pnpm build` from
  `docs/`, plus `pnpm test` from `docs/` as a CI step — but no E2E project) — both need a live API and
  database, not just a build. `ci.yml` also runs a `docker` job (builds and smoke-tests the two
  container images — chapter 20's "The two container images" section) and two `sonar-*` jobs; none of the three
  is a standing gate.

See `docs/guide/en/22-testing.md` for all four layers in more depth — it covers the Contract
layer both in its own "The schema contract" section and in its "What CI runs" section. `schema/README.md`
remains the authoritative reference for the contract itself and its regeneration command.

## Commit message format

Conventional commits, observed consistently in this repository's own history: `<type>(<scope>):
<description>`, scope optional. Types actually used: `feat`, `fix`, `docs`, `chore`, `test`, `refactor`,
`ci`, `style`, `perf`, `build`, `revert`. No attribution trailer is used in this repository's commits.

## Next steps

- `docs/ai/architecture.md` for the layer map and extension points these conventions apply to.
- `docs/ai/task-playbooks.md` for these conventions applied end-to-end in five concrete recipes.
