# Phase 7e — Media Library + File/Image field pickers (design)

**Date:** 2026-07-03
**Status:** approved (brainstorm), pending implementation plan
**Scope:** frontend-only. The backend file API (Phase 5) and `File` as a CMS collection (Phase 5/5.6) already provide everything required — **no backend changes**.

---

## 1. Goal

Replace the read-only fallback for the `File` and `Image` field interfaces with a real experience, structured as two cooperating pieces:

1. A **Media Library** — a dedicated, Collection-like admin view where users upload (including drag-and-drop bulk upload), browse, and manage files.
2. A **File / Image field picker** used inside every *other* collection's form: it **selects an existing file** from the library (never uploads directly), storing the chosen File's `Guid`.

`Files` (multi-value) is explicitly **out of scope** (deferred until its storage model is decided). Only single-reference `File` and `Image` (`Guid?`) are built.

## 2. What already exists (no work needed)

- **`File` CMS collection** — `[CmsCollection("File", Group = "System", DefaultDisplayField = nameof(FileName))]` in `src/Struo.Infrastructure/Files/File.cs`. Fields: `FileName` (searchable, read-only), `ContentType`, `Size`, `Width`, `Height`, `Status` (select). Sidecar `FileTranslation` carries per-locale **`Title`** (searchable) + **`Alt`**.
  - ⇒ `GET /api/items/file` (paginated / sortable / searchable via Phase 7b infra), `GET /api/items/file/{id}` (deep + locale), `PUT /api/items/file/{id}` (edit Title/Alt), delete path.
- **File endpoints** — `src/Struo.Api/Controllers/FilesController.cs`:
  - `POST /api/files` (multipart, form part `file`) → `201 { data: { id, fileName, contentType, size, width, height, status } }`. **Auth required.**
  - `GET /api/files/{id}` → metadata (published only).
  - `GET /api/files/{id}/content` → `302` redirect to a presigned URL (S3/MinIO) or a streamed file. Used for thumbnails/previews.
  - `DELETE /api/files/{id}` → `204 / 404`. **Auth required.**
- **Frontend infra** — `itemsApi` (list with pagination/sort/search/filter/locale, get deep, delete), `apiClient` (cookie/bearer, camelCase), RBAC-filtered nav (`CollectionNav`), the schema-driven item form (`ItemForm` / `FieldInput`), and the picker pattern established by Phase 7d (`RelationPicker` lazy dialog).

## 3. Architecture

Two frontend units, both reading the same data layer; neither touches the save path beyond storing a `Guid?`.

### 3.1 Media Library view

- **Route:** dedicated `/media` with its own top-level "Media Library" nav entry (not folded into the generic collection list). The generic `file` collection list is suppressed from the auto nav so there is a single, media-specific entry point.
- **Listing:** reuses `itemsApi.list('file', …)` for pagination / sort / search (search hits `FileName` + translated `Title`). Rendered as a **responsive thumbnail grid/gallery**, not a DataTable:
  - image content types → `<img>` preview sourced from `GET /api/files/{id}/content`;
  - non-image → a file-type + size chip.
- **Upload:** a drop zone covering the grid plus an explicit "Upload" button.
  - Dropping/selecting N files issues **N parallel `POST /api/files`** requests.
  - A transient per-file upload row shows progress state and, on failure, an inline error (e.g. rejected type/size, 401) without aborting the sibling uploads.
  - On each success the grid refreshes (or the new tile is prepended).
- **Per-file actions:** open a detail/preview; **edit metadata** (per-locale Title/Alt) via the existing item-edit form bound to the `file` collection (`GET`/`PUT /api/items/file/{id}`); **delete** via `DELETE /api/files/{id}` with a confirm.
- **Permissions:** the view and its actions respect RBAC on the `file` collection (read to browse, write to upload/edit, delete to remove); `POST /api/files` and `DELETE` already enforce auth server-side. Missing grants surface as inline errors, consistent with prior phases.

### 3.2 File / Image field picker

- **Dispatch:** `FieldInput.vue` maps `fieldInputKind('file') === 'file'` and `'image'` to a new **`FilePicker`** component (mirrors `RelationPicker`).
- **Behaviour — select-only** (other collections pick, never upload):
  - Renders the current value: for `Image`, a thumbnail (via `/api/files/{id}/content`) + file name; for `File`, a name/size chip.
  - **"Select"** opens a dialog that reuses the same media grid (searchable, paginated) to choose one existing file → stores its `Guid` in the field model.
  - **"Clear"** sets the value to `null`.
  - No upload affordance inside the picker; a hint links to the Media Library for adding files.
- **Persistence:** the field value is a `Guid?` exactly like SEO's `SeoOgImageId`. `buildItemPayload` already serialises scalar fields, so **no change to the save path** — the picker only reads/writes the model value.
- **Edit round-trip:** on edit, the form already loads the stored `Guid`; the picker resolves the file's display (name/thumbnail) by fetching `GET /api/files/{id}` (metadata) lazily, with a graceful fallback to showing the raw id if the referenced file was deleted (same defensive posture as `RelationPicker.ensureSelectedLabels`).

## 4. Components (frontend)

New / changed under `frontend/src/`:

- `views/MediaLibraryView.vue` — the `/media` page: grid + drop zone + upload orchestration + delete/edit entry points.
- `components/media/MediaGrid.vue` — presentational thumbnail grid (image preview vs. chip), reused by both the view and the picker dialog. *(Split out so the picker and the library share one grid.)*
- `components/media/FileThumbnail.vue` — a single tile (image `<img>` or type chip + name/size), with a lazy/cached content URL helper.
- `components/media/MediaUploadDropzone.vue` — drop zone + file input; emits selected `File[]`; owns per-file progress/error rows.
- `components/fields/FilePicker.vue` — the field control (current value + Select dialog + Clear).
- `api/filesApi.ts` — thin wrapper over `apiClient` for `POST /api/files` (multipart) and `DELETE /api/files/{id}`, plus a `contentUrl(id)` helper. (Listing/metadata/edit go through the existing `itemsApi`.)
- `FieldInput.vue` — add `file` / `image` dispatch to `FilePicker`.
- `router` + `CollectionNav` — add the Media Library route/entry; suppress the auto `file` collection entry.

Each unit has one purpose and a narrow interface; `MediaGrid` is the shared seam between the library view and the picker dialog.

## 5. Error handling (per §5 coding-style)

- Upload: per-file inline error on non-2xx (bad type/size → 400, unauthenticated → 401), siblings unaffected; the drop zone never silently drops a file.
- Missing/deleted referenced file in a picker: fall back to showing the id (no crash), matching `RelationPicker`.
- Thumbnail load failure: show the type chip instead of a broken image.
- Delete: confirm dialog; surface 404/permission errors inline; refresh on success.
- All network calls go through `apiClient`, inheriting its auth + error envelope handling.

## 6. Sample additions (for the live gate)

Add a single-reference `Image` field to the sample `Article`:

- `Article.HeroImageId : Guid?` with `[CmsField(Label = "Hero Image", Interface = FieldInterface.Image, Group = ...)]`.

This lets the live gate exercise the full loop end-to-end without inventing a second convention.

## 7. Testing

- **Unit/component (Vitest):** `FilePicker` (renders current value, opens dialog, select stores Guid, clear nulls, deleted-file fallback); `MediaUploadDropzone` (multi-file selection → N uploads, per-file error isolation); `MediaGrid`/`FileThumbnail` (image preview vs. chip); `FieldInput` dispatch to `FilePicker` for `file`/`image`; `filesApi` (multipart shape, contentUrl).
- **E2E (Playwright, user-driven live gate):** drag-drop multi-upload in the Media Library → files appear; edit Title/Alt; open an Article, pick the hero image, save; re-open and confirm it round-trips; clear it; delete a file in the library.
- **Backend:** unchanged; existing suite must stay green (regression guard only).

## 8. Verification gate

1. Backend `dotnet build && dotnet test` — clean / all green (no backend change; regression guard).
2. Frontend `pnpm test && pnpm build` — all component tests pass; build succeeds.
3. **Live gate (real Postgres + Redis + MinIO/S3):** the E2E loop in §7 against the dev API, with evidence recorded (upload 201s, `/api/items/file` list, thumbnail 302, article deep-read showing `heroImageId`, delete 204). Live gate is user-driven.
4. Flip the ROADMAP Phase 7e row to done + live-verified; link spec/plan.

## 9. Out of scope (explicit)

- `Files` (multi-value) interface and its storage model.
- Inline upload from within the field picker.
- Image cropping/transforms, folders/tagging, orphan garbage-collection (files are managed by deleting them in the library).
- TipTap rich text and multi-value/structured editors (later sub-phases 7f/7g).

---

## Self-review

- **Placeholders:** none — every component, endpoint, and value shape is named concretely.
- **Consistency:** the picker stores `Guid?` identical to the existing `SeoOgImageId`; `MediaGrid` is the single shared unit between library and picker; all reads reuse `itemsApi`, only upload/delete/contentUrl use the new `filesApi`; no save-path change (matches §4/§3.2).
- **Scope:** single implementation plan — one new view, four small components, one field control, one api wrapper, one dispatch line, one sample field. Comparable in size to the Phase 7d relations slice.
- **Ambiguity:** "Collection-like media view" resolved to a dedicated `/media` route reusing the `file` collection's data layer (not the generic DataTable); picker resolved to strictly select-only.
