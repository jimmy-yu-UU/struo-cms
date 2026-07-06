# Phase 7g — Advanced rich text: tables, text-align, colour, sub/superscript (design)

**Date:** 2026-07-06
**Status:** approved (brainstorm), pending implementation plan
**Scope:** the second rich-text slice, deferred from 7f. Extends the TipTap editor and the server-side
sanitizer allowlist **in lockstep** with basic tables, text alignment, text colour, and
sub/superscript. Multi-value selects (`MultiSelect`/`CheckboxGroup`/`Tags`), structured editors
(`Json`/`KeyValue`/`Repeater`), and multi-file `Files` remain deferred (7g+), rendering read-only
meanwhile.

---

## 1. Goal

Grow the 7f rich-text editor from "basic formatting + inline images" into a genuinely
content-team-usable editor: **basic tables** (insert with header row, add/delete rows and columns,
toggle header row, delete table), **text alignment** (left/center/right/justify on paragraphs and
headings), **text colour** (fixed palette + free colour picker + clear), and **sub/superscript**
(mutually exclusive marks).

The security-critical counterpart: the `GanssHtmlSanitizer` allowlist is extended to *exactly* the
new editor output, and no further. The 7f lesson is binding here — **when the sanitizer allowlist
must mirror editor output, any mismatch is silent data loss** (sanitizer strips what the editor
legitimately emits) or an XSS hole (sanitizer passes what the editor never emits). Every new
capability lands as an editor-change + sanitizer-change + test triplet.

The biggest single decision (user-approved): the sanitizer moves from "strip ALL inline CSS" to
**"allow the `style` attribute carrying exactly two CSS properties: `color` and `text-align`"**.
TipTap's TextAlign/Color extensions emit `style=` natively, and in a headless CMS the stored HTML is
rendered by arbitrary consumer frontends — inline styles render everywhere without shipping any
companion CSS, unlike a class-based scheme. Ganss.Xss parses `style` per-property: non-allowlisted
properties (`font-size`, `position`, `background-image`, …) and dangerous values (`url(…)`,
`expression(…)`) are stripped property-by-property, so this is a *scoped* relaxation, not a door.

## 2. What already exists (context)

- **Editor** — `frontend/src/components/fields/RichTextInput.vue` (7f): TipTap `StarterKit`
  (heading levels [2,3], bundled underline/link disabled), hardened `Link`
  (http/https/mailto, no autolink), `Image` (block; relative `src` + `data-file-id` via
  `lib/richTextImages.ts`). Toolbar: bold, italic, strike, H2, H3, bullet/ordered list, blockquote,
  code block, link, hr, insert-image (7e `MediaGrid` dialog), undo, redo. Buttons carry `data-cmd`
  attributes (test hooks).
- **Sanitizer** — `src/Struo.Infrastructure/Security/GanssHtmlSanitizer.cs`: tags
  `p h2 h3 strong em s ul ol li blockquote pre code hr br a img`; attributes `href src alt rel` +
  all `data-*`; schemes `http https mailto`; **`AllowedCssProperties`/`AllowedAtRules` cleared**
  (all inline CSS stripped); every surviving `<a>` post-processed to `rel="noopener noreferrer"`,
  `target` removed. Singleton, configured once in the constructor, never mutated.
- **Write path** — `ItemService` sanitizes every `RichText` string on write on both the
  parent-entity (`Deserialize`) and translation-sidecar (`SyncTranslationsAsync`) paths; blank
  documents coerce to `null`. **This slice does not touch it** — the new allowlist applies
  automatically.
- **Tests** — backend 316 green (`GanssHtmlSanitizerTests`, `ItemServiceRichTextSanitizationTests`,
  PG integration suite live); frontend 161 green (4 `RichTextInput` tests among them).

## 3. Architecture

### 3.1 Backend — sanitizer allowlist extension (the only backend change)

All in `GanssHtmlSanitizer.cs` (constructor configuration + tests); no signature or DI changes.

- **Tags added:** `table, thead, tbody, tr, th, td` (basic tables), `sub, sup` (sub/superscript),
  `span` (the carrier tag for TipTap colour output `<span style="color: …">`).
- **Attributes added:** `style`.
- **CSS:** `AllowedCssProperties = { color, text-align }` (replacing the blanket clear).
  `AllowedAtRules` stays cleared. Ganss validates values per-property; `url()`/`expression()` and
  any non-allowlisted property are stripped while the rest of the `style` attribute survives.
- **Deliberately NOT allowed:** `colspan`, `rowspan`, `colwidth`, `col`, `colgroup` (basic tables
  don't merge cells or resize columns). If TipTap emits `colspan="1" rowspan="1"` on cells, the
  sanitizer strips them; re-parsing defaults them to 1, so the round-trip is lossless — a test
  pins this behaviour.
- **Unchanged:** anchor hardening post-processor, scheme allowlist, `data-*` allowance, blank→null
  coercion in `ItemService`.

### 3.2 Frontend — editor extensions

**Packages** (installed via `pnpm add`, versions produced by pnpm, never hand-authored — §17.5;
the plan must verify the *actual* TipTap v3 package layout at install time, e.g. whether Color
ships inside `@tiptap/extension-text-style` and whether the table packages are consolidated —
and, per the 7f StarterKit lesson, audit each new package's default-enabled behaviour against the
sanitizer allowlist):

- Tables: `@tiptap/extension-table` (+ row/cell/header as the installed layout dictates),
  configured with **`resizable: false`** (no `colwidth` output).
- Alignment: `@tiptap/extension-text-align`, `types: ['heading', 'paragraph']`,
  alignments left/center/right/justify. Left is the default and emits no `style`.
- Colour: TextStyle + Color (emits `<span style="color: …">`).
- Sub/superscript: the subscript + superscript extensions; the toolbar enforces mutual exclusion
  (setting one unsets the other — matching TipTap's own default behaviour, pinned by a test).

**Component structure** (user-approved: extract the complex controls, keep simple toggles inline):

- `RichTextInput.vue` — toolbar grows by six simple toggle buttons (align-left/center/right/justify,
  sub, sup; all with `data-cmd` hooks) and mounts the two new menu components. Existing buttons and
  behaviour untouched.
- `RichTextColorMenu.vue` (new) — a popover with a fixed palette (~10 swatches), a free colour
  input (`<input type="color">`), and a "clear colour" action. Emits the chosen hex; the parent
  runs the editor command. Follows the `disabled` prop.
- `RichTextTableMenu.vue` (new) — a dropdown menu: insert 3×3 table with header row; add row/column
  before/after; delete row/column; toggle header row; delete table. Items that require being inside
  a table are disabled when the selection is outside one. Follows the `disabled` prop.

**No dispatch/save-path change:** the field contract stays a plain HTML string through
`FieldInput` → `ItemForm` → `buildItemPayload`; `fieldInputKind` is untouched.

## 4. Data flow

Identical to 7f — the editor emits an HTML string (now possibly containing tables, `style`
attributes with `color`/`text-align`, and `sub`/`sup`), the existing save path posts it, and
`ItemService` sanitizes on write through the extended allowlist. Read-back round-trips through the
existing projection/overlay; no new rendering surface is added outside the form editor.

## 5. Error handling & security

- **Threat model unchanged:** stored XSS, defended on write. The relaxation is scoped to two CSS
  properties whose values Ganss validates; everything else in `style` is stripped per-property.
- **Assertions that must hold** (tests): `style="position:fixed"`, `style="background:url(//evil)"`,
  `style="width:expression(alert(1))"`, `font-size`, and mixed declarations keep only the allowed
  properties; all 7f XSS vectors (`<script>`, event handlers, `javascript:`/`data:` URIs,
  `<iframe>`) remain stripped; `colspan`/`rowspan` are stripped harmlessly.
- **UI:** the new menus follow the existing `disabled` prop; no new error surface server-side
  (the sanitizer is a pure transform).

## 6. Testing & live-gate

- **Backend unit** (`GanssHtmlSanitizerTests` extension, TDD first): basic-table markup
  round-trips; `text-align`/`color` styles survive; non-allowlisted CSS properties and dangerous
  values are stripped (per §5); `sub`/`sup` survive; `colspan`/`rowspan` stripped; the full 7f
  vector suite still passes (regression).
- **Frontend unit/component** (`vitest`): `RichTextColorMenu` (palette pick, free pick, clear,
  disabled), `RichTextTableMenu` (insert, in-table enablement, row/col ops, delete, disabled),
  `RichTextInput` extended (new toggles dispatch the right editor commands; sub/sup mutual
  exclusion; align active-state).
- **Gates:** backend `dotnet build` clean + `dotnet test` green (316 baseline + new); frontend
  `pnpm test` green (161 baseline + new) + `pnpm build` succeeds.
- **Live-gate (real Postgres + Redis + MinIO, API-level, per project convention):**
  1. Create an `article` whose `body` contains a table + aligned heading/paragraph + coloured span
     + sub/sup → read-back preserves all of it byte-for-byte (the allowlist-mirror gate).
  2. A hostile `body` (`position:fixed`, `background:url(…)`, `expression(…)`, plus the 7f vector
     suite) reads back with only the allowed subset surviving.
  3. Per-locale (`en` + `zh-TW`) body with the new formatting round-trips with correct UTF-8.

## 7. Out of scope (deferred)

- Cell merge (`colspan`/`rowspan`) and column resizing (`colwidth`); background-colour/highlight;
  font-size/font-family — future rich-text increments if a real need appears.
- **7g+:** multi-value selects (`MultiSelect`/`CheckboxGroup`/`Tags`), structured editors
  (`Json`/`KeyValue`/`Repeater`), multi-file `Files` — plus the audit-F1 field-type registry
  refactor recommended before that work.
- In-editor uploading (uploads remain in `/media`); Markdown/Code interfaces (still `Textarea`).
