# Phase 7f — TipTap rich text (basic formatting + inline images) + server-side HTML sanitization (design)

**Date:** 2026-07-03
**Status:** approved (brainstorm), pending implementation plan
**Scope:** the first of two rich-text slices. Delivers a genuinely usable rich-text editor *and* the
security-critical write-path HTML sanitization pipeline. Advanced formatting (tables, text-align,
colours, sub/superscript) is deferred to a follow-up slice (7g). Multi-value selects, structured
editors, and multi-file `Files` remain deferred (7g+), rendering read-only meanwhile.

---

## 1. Goal

Replace the read-only-ish fallback for the `RichText` field interface — today it degrades to a plain
PrimeVue `Textarea` (`frontend/src/components/fields/FieldInput.vue:26`) — with a real TipTap WYSIWYG
editor, and **close the CLAUDE.md §8 gap**: RichText is currently stored as a raw `string?`
(`ArticleTranslation.Body`) with **no server-side sanitization**. Once the editor emits real HTML,
persisting it unsanitized would be a **stored-XSS** vulnerability, so the sanitization pipeline is
part of this slice, not a later one.

Two cooperating deliverables:

1. **Backend** — a write-path HTML sanitizer (the security core) applied to every `RichText` field on
   both the parent-entity and translation-sidecar write paths.
2. **Frontend** — a `RichTextInput.vue` TipTap editor (basic formatting + inline images that reuse the
   Phase 7e media library), wired in through the existing schema-driven form with **no save-path change**
   (the field contract stays a plain HTML string, exactly like 7e kept a scalar `Guid?`).

## 2. What already exists (context)

- **RichText field + sample** — `FieldInterface.RichText` (`src/Struo.Domain/Metadata/Enums/FieldInterface.cs`);
  the sample `ArticleTranslation.Body` is `[CmsField(Interface = FieldInterface.RichText)]`, `string?`,
  **translatable** (so it flows through `SyncTranslationsAsync`, not the parent `Deserialize` path).
- **Write path** — `src/Struo.Application/Query/ItemService.cs`:
  - non-translatable fields are set on the entity in `Deserialize` (line ~549);
  - translatable field values are collected in `SyncTranslationsAsync` at `fieldValues[field.Name] = JsonValue(field.Value)` (line ~403) before `repository.SyncTranslationsAsync`.
  - Both have `meta.Fields` in scope, so both can identify `Interface == RichText` string values.
- **Frontend form infra** — `FieldInput.vue` dispatches by `fieldInputKind(interface)`; `richText → 'richtext'`
  currently renders a `Textarea`. `ItemForm` treats the field value as an opaque string via `v-model`.
- **Media library (7e)** — `MediaGrid.vue` (browse/select existing files, returns a file `Guid`), and the
  auth-aware `GET /api/files/{id}/content` (302 → presigned URL; authenticated callers see any status,
  anonymous sees published only). Inline images reuse both.
- **No sanitizer today** — no `Ganss.Xss`/`HtmlSanitizer` dependency exists (confirmed by grep). This slice adds it.

## 3. Architecture

### 3.1 Backend — HTML sanitization (security core)

- **Port** `IHtmlSanitizer` in `Struo.Application` (Security) — `string Sanitize(string html)`. Keeps the
  §2 dependency rule: Application defines the port, Infrastructure supplies the implementation, no external
  package leaks into Application/Domain.
- **Implementation** `GanssHtmlSanitizer` in `Struo.Infrastructure`, backed by **Ganss.Xss** (the de-facto
  .NET sanitizer). Installed via `dotnet add package Ganss.Xss` — the produced version is centralized in
  `Directory.Packages.props` (§17.5; never hand-authored).
- **Allowlist** — configured once in `GanssHtmlSanitizer`, matched one-to-one to the TipTap output of §3.2:
  - **Tags:** `p, h2, h3, strong, em, s, ul, ol, li, blockquote, pre, code, hr, br, a, img`.
  - **`a`:** attribute `href` only; allowed URI schemes restricted to `http`, `https`, `mailto`. Add
    `rel="noopener noreferrer"` (and drop any `target` other than `_blank`) — configured, not hand-rolled.
  - **`img`:** attributes `src`, `alt`, `data-file-id` only. `src` restricted to **relative URLs** — no
    `javascript:`, no `data:` (both stripped). This is the key XSS control for images.
  - Everything else (`script`, `style`, `iframe`, event-handler attributes like `onerror`/`onclick`,
    inline `style`, unknown tags/attributes) is stripped.
- **Application point** — `IHtmlSanitizer` is injected into `ItemService` and applied at both write points:
  - **Non-translatable RichText:** after `Deserialize`, for each `meta.Fields` where `Interface == RichText`,
    read the mapped entity property; if the value is a non-null string, replace it with `Sanitize(value)`.
  - **Translatable RichText:** in `SyncTranslationsAsync`, when storing `fieldValues[field.Name]`, if the
    field's interface is `RichText` and the value is a string, store `Sanitize(value)`.
  - A single small private helper (e.g. `SanitizeRichText`) is reused by both paths to avoid duplication.
- **Empty-value convergence** — a "blank" TipTap document serializes to `<p></p>`. After sanitization,
  if the result is empty/whitespace-only or an empty-paragraph shell, coerce to `null` (the field is
  `string?`). This follows the 7c/7e empty-value discipline (avoid dirty `<p></p>` rows) and ensures the
  **required** check treats a sanitized-empty RichText value as empty (rejected for required fields).

### 3.2 Frontend — `RichTextInput.vue` (TipTap)

- **Packages:** `pnpm add @tiptap/vue-3 @tiptap/starter-kit @tiptap/extension-link @tiptap/extension-image`
  (versions produced by pnpm, never hand-authored — §17.5).
- **Component:** `frontend/src/components/fields/RichTextInput.vue`. `fieldInputKind` keeps `richText → 'richtext'`;
  `FieldInput.vue` routes `kind === 'richtext'` to `RichTextInput` instead of `Textarea`.
- **Model contract:** `modelValue: string` in / `update:modelValue` (HTML string) out — identical to the
  Textarea it replaces, so `ItemForm`/`ItemFormView` and the save path are unchanged.
- **StarterKit config:** headings restricted to `levels: [2, 3]`; keep bold/italic/strike, bullet + ordered
  lists, blockquote, code block, horizontal rule, undo/redo. Link via `@tiptap/extension-link`
  (schemes http/https/mailto, matching the allowlist). Image via `@tiptap/extension-image`.
- **Toolbar:** bold, italic, strike, H2, H3, bullet list, ordered list, blockquote, code block, link
  (add/remove), horizontal rule, insert image, undo, redo. Buttons reflect active/disabled state.
- **Disabled/read-only:** honours `disabled` / `field.readOnly` (editable=false), matching other inputs.

### 3.3 Inline images (reuse 7e media library)

- The toolbar "insert image" opens the existing `MediaGrid` (in a dialog) to **select** an existing file
  (never uploads from inside the editor — uploads stay in `/media`). Selection yields a file `Guid`.
- Inserted markup: `<img src="/api/files/{id}/content" data-file-id="{id}" alt="...">` — an **app-relative**
  content URL plus `data-file-id` (stable, no presigned-URL expiry, portable across environments; origin
  is never baked into stored content).
- **Editor preview:** because the SPA is cross-origin to the API, the editor prefixes the relative `src`
  with the API base **only for display**; the stored value stays relative. Image bytes come from the 7e
  auth-aware `GET /api/files/{id}/content` (logged-in editors can preview any status).

### 3.4 Read-only / list rendering

- RichText is not a typical list column; the collection list keeps the existing `formatCell` behaviour —
  **no new HTML rendering surface** is introduced outside the form editor. (Stored HTML is already
  sanitized on write, so any future render point is safe, but none is added here.)

## 4. Data flow

1. User edits body in `RichTextInput` → HTML string in the form model (per-locale for translatable fields
   via the existing i18n locale tabs).
2. Save → `ItemForm` builds the same payload as today (`translations: { locale: { body: "<html>" } }`
   for the sample, or a scalar field for a non-translatable RichText field).
3. `ItemService` sanitizes each RichText value on write (§3.1); empty → `null`.
4. Read back → sanitized HTML round-trips through the existing projection/overlay; the editor loads it
   (prefixing relative image `src` for preview).

## 5. Error handling & security

- **Stored XSS is the primary threat.** Sanitization happens **on write** (store-clean), so every read
  path is safe by construction. Assertions: `<script>`, `onerror=`/inline event handlers, `javascript:`
  and `data:` `img` src, `<iframe>`, and inline `style` are all stripped; the legitimate allowlist passes
  through unchanged.
- **Required validation** must treat sanitized-empty as empty (§3.1), so a body of only stripped/blank
  markup cannot satisfy a required RichText field.
- **URL scheme restriction** on both `a[href]` (http/https/mailto) and `img[src]` (relative only).

## 6. Testing & live-gate

- **Backend unit** (`Struo.Tests`): `GanssHtmlSanitizer` allowlist passes legitimate tags; strips
  `<script>`, event-handler attributes, `javascript:`/`data:` img src, `<iframe>`, inline `style`,
  unknown tags. `ItemService` sanitizes on create/update for both translatable and non-translatable
  RichText; empty→null; required-empty rejected.
- **Frontend unit/component** (`vitest`): `RichTextInput` v-model round-trip; toolbar commands toggle
  marks/nodes; image-insert flow (mock `MediaGrid`) produces the expected relative-src `<img>`;
  disabled state.
- **Live-gate (real Postgres + Redis + MinIO), API + UI:**
  1. Create an `article` whose `body` contains a `<script>`/`onerror` payload → read back confirms the
     payload is stripped (the stored-XSS defense — the acceptance gate).
  2. Insert a media-library image → stored `<img src="/api/files/{id}/content" data-file-id=...>`
     round-trips; preview loads via the 302 content endpoint.
  3. Per-locale body (`en` + `zh-TW`) round-trips with correct UTF-8.
- **Regression:** full backend `dotnet test` and frontend `pnpm test` + `pnpm build` stay green.

## 7. Out of scope (deferred)

- **7g (advanced rich text):** tables, text-align, colour, sub/superscript — incremental additions to the
  same sanitizer allowlist + toolbar.
- **7g+:** multi-value selects (`MultiSelect`/`CheckboxGroup`/`Tags`), structured editors
  (`Json`/`KeyValue`/`Repeater`), multi-file `Files`.
- In-editor uploading (uploads remain in the `/media` library); Markdown/Code interfaces (still `Textarea`).
