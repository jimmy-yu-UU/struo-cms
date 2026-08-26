# 14. Admin SPA Customization

The admin SPA (`frontend/`) is a Vue 3 + Tailwind v4 + shadcn-vue + Pinia application driven almost entirely by the
metadata the API exposes at `GET /api/schema`. This chapter is about the remaining part that is
genuinely code, not metadata: theming, i18n, field editors, branding, and how the dev server reaches
the API — and where in `frontend/src` each of those lives.

## When to customize vs. when metadata is enough

Most of what looks like "admin UI work" is not. Chapter 4 shows that adding a `[CmsCollection]` with
`[CmsField]`s is enough on its own to make a full collection — sidebar entry, paginated list with
schema-driven columns, and a create/edit form — appear with zero Vue code touched. Chapter 2's "no
`Content` navigation group at all" with zero collections, and chapter 4's "confirm the admin SPA's
sidebar now shows a `Content` navigation group with your collection in it" once one exists, describe the
same schema-driven rendering, before and after adding one. The admin SPA never hardcodes a collection's
fields, columns or labels; the schema endpoint is the only place that information comes from.

Reach into `frontend/src` only when the requirement is not expressible as metadata:

- A field needs an editor experience none of the 33 shipped interfaces provide (chapter 5's table —
  e.g. a real color swatch picker instead of `Color`'s plain text input) — replace or extend an entry
  in the field-type registry (below).
- The brand needs more than a name and a logo (custom CSS, extra topbar content) — theming.
- The admin UI itself needs to speak another human language — i18n.
- A workflow doesn't fit the generic list/form pattern at all (a dashboard widget, a bespoke wizard) —
  a new view or component, same as any Vue application.

Everything else — new collections, new fields, relations, validation, permissions — is chapter 4/5/7/12
territory and needs no change under `frontend/src`.

## Directory map of `frontend/src`

| Directory | Contents |
|---|---|
| `api/` | One thin module per REST resource — `apiClient.ts` is the shared envelope-aware fetch wrapper; `itemsApi.ts`, `schemaApi.ts`, `filesApi.ts`, `languagesApi.ts`, `rbacApi.ts`, `settingsApi.ts`, `appConfigApi.ts` — typed calls, no business logic. |
| `assets/` | `theme.css` — the app's unlayered global layer: the page/surface/foreground and status/shadow/overlay/font custom properties (`--bg`, `--surface`, `--fg`, `--warn`, `--danger`, `--shadow-*`, `--overlay`, `--font`, `--mono`, …) first-party scoped CSS reads, plus the `html`/`body` resets, the theme-transition rule, and `.app-breadcrumb`. `tokens.css` — the Tailwind v4 entry point (`@import "tailwindcss"`) plus the shadcn semantic token layer (`--background`, `--primary`, `--radius`, …) that the vendored `ui/` components and Tailwind utilities both read. |
| `components/` | `ItemForm.vue` (the generated item form) directly under `components/`, plus `ui/` (vendored shadcn atoms — `button`, `table`, `select`, `dialog`, `sidebar`, … — generated output; **read-only**, no edits and no `:deep()` into it), `data/` (`DataTable`, `SortableHeader`, `DataTablePagination`, `FilterBuilder` — the TanStack-table-backed list primitives `CollectionListView` is built on), `fields/` (one editor component per field interface, chapter 5), `common/` (`PageHeader`, `ListToolbar` — plain Tailwind/shadcn components; `MediaLibraryView` uses `ListToolbar` for its search box and filter slot, while `CollectionListView` is built on `data/`'s primitives instead), `shell/` (topbar, sidebar nav item, theme toggle, UI language switcher, brand mark), `media/`, `revisions/`, `rbac/`. |
| `composables/` | Cross-cutting reactive logic, e.g. `useConfirm.ts`. |
| `i18n/` | `index.ts` — the `vue-i18n` instance (`legacy: false`), wired to `locales/`. |
| `layouts/` | `AppShell.vue` — the topbar + sidebar + content grid every authenticated route renders inside. |
| `lib/` | Framework-free helper functions: `fieldTypes/` (the field-type registry, below), plus the formatting/validation/query helpers views and field components share. |
| `locales/` | `en.ts` / `zh-TW.ts` — the admin UI's own message catalogs, distinct from content languages (chapter 6). |
| `router/` | `index.ts` (routes), `guard.ts` (the auth/permission navigation guard). |
| `stores/` | Pinia stores: `authStore`, `appConfigStore`, `schemaStore`, `themeStore`, `uiLocaleStore`, `sidebarStore`, `languageStore`. |
| `theme/` | `resolveInitialTheme.ts` / `resolveInitialUiLocale.ts` — first-paint `localStorage`/media-query resolution, read before any store exists. |
| `types/` | `schema.ts` — TypeScript mirrors of the backend DTOs (`FieldMeta`, `CollectionMeta`, `RelationMeta`, …). |
| `views/` | One component per route: `DashboardView`, `CollectionListView`, `ItemFormView`, `MediaLibraryView`, `SettingsView`, `LoginView`. |

## Design tokens and theming

Two layers cooperate, and there is nothing separate to keep in sync — both flip on the same `.app-dark`
toggle class:

1. **`frontend/src/assets/tokens.css`** — the Tailwind v4 entry point (`@import "tailwindcss"`) and the
   shadcn semantic custom properties (`--background`, `--foreground`, `--primary`, `--radius`,
   `--sidebar-*`, …), declared once on `:root` for light and re-declared on `.app-dark` for dark.
   `@theme inline` maps each property onto the Tailwind utility classes (`bg-background`,
   `text-primary`, …) that both `src/components/ui/` and first-party components consume.
2. **`frontend/src/assets/theme.css`** — the app's unlayered global layer: the page/surface/foreground
   and status/shadow/overlay/font custom properties (`--bg`, `--surface`, `--fg`, `--warn`, `--danger`,
   `--shadow-*`, `--overlay`, `--font`, `--mono`) first-party scoped CSS reads, plus the `html`/`body`
   resets, the theme-transition rule, and `.app-breadcrumb`. It loads after `tokens.css`, and because
   none of its rules sit inside a Tailwind `@layer`, they outrank any utility class applied to the same
   element regardless of specificity — an unlayered declaration wins over a layered one (unless the
   layered side carries `!important`, which inverts this), which is the single most common way a
   hand-written CSS override silently does nothing.

Both are imported once, in `frontend/src/main.ts`:

```ts
import './assets/tokens.css'
import './assets/theme.css'
```

`themeStore.apply()` toggles the `.app-dark` class on `<html>`, which flips both files' custom
properties in one step (a plain CSS selector match) — nothing else needs to be told the mode changed.
The initial mode is resolved before Pinia/Vue even exist, in `theme/resolveInitialTheme.ts`: a saved
`struo.theme` in `localStorage`, else `prefers-color-scheme`, else `light`.

To re-theme the SPA, edit the semantic custom properties in `tokens.css`'s `:root`/`.app-dark` blocks —
the contract every vendored `ui/` component and Tailwind utility reads — and, if the change also
touches first-party chrome such as the sidebar or breadcrumb, the corresponding properties in
`theme.css`.

Chapter 3's `Branding:Name`/`Branding:LogoUrl` reach only the product name and logo, never the color
palette — the palette is a template default edited in source, not a per-deployment configuration key.

### The rich-text editor's typography

The `richText` field's editable surface (`frontend/src/components/fields/RichTextInput.vue`) carries
`class="prose dark:prose-invert"` on its ProseMirror content element, rendering with
`@tailwindcss/typography`'s factory defaults — no custom CSS file, class, or `--tw-prose-*` override
of its own. The intent is for the editing surface to resemble a rendered article rather than plain
unstyled HTML.

This is a suggestion, not a requirement, and the choice stays with the frontend developer: if your
site's own frontend also renders content with Tailwind and `@tailwindcss/typography`, the editor's
appearance will track the published article closely, because both sides are using the same plugin's
defaults on the same stored HTML. If your frontend uses a different rendering stack, override the
`--tw-prose-*` custom properties on `.prose` in `frontend/src/assets/tokens.css`, where the plugin
is registered (`@plugin "@tailwindcss/typography"`), to pull the editor's look toward your own
layout instead. Write the override as a plain rule, not inside a `@layer` or `@utility` block: the
plugin's own `.prose` rule is emitted inside `@layer utilities`, and the same rule as `theme.css`
above applies — an unlayered declaration outranks a layered one regardless of specificity — so a
plain rule wins here too, while a layered override would silently lose. Those properties are
`@tailwindcss/typography`'s own mechanism (see that package's documentation), not a contract
StruoCMS defines or guarantees.

Three gaps between the editor and a rendered article are deliberately left as accepted trade-offs, not
defects:

- **Dark mode.** `dark:prose-invert` guarantees the dark editor stays readable; it is not tuned to
  match any particular frontend's dark theme. The faithful preview of the plugin's defaults is the
  light theme.
- **Font.** The editor inherits the admin SPA's own font stack. The template deliberately does not
  guess a font on a fork's behalf.
- **Background.** The editing surface sits on the admin's card surface, not on a site's page
  background — the page chrome a frontend puts around an article is outside what this template
  controls.

Table header cells used to be a fourth accepted gap here; they no longer are, and the reason is worth
explaining because it is not "one plugin's defaults matching on both sides" the way the rest of this
section is. TipTap's table extension has no `thead` node in its schema, so a header row always
round-trips through the editor as `<th>` cells inside `<tbody>` — the editor cannot produce a `<thead>`
no matter how the table was authored. StruoCMS closes that gap on the server instead, at write time:
the sanitizer's post-processing step (`TableHeadNormalizer`, in
`src/Struo.Infrastructure/Security/TableHeadNormalizer.cs`) wraps a table's first row in `<thead>`
whenever every cell in that row is a `<th>` and the table has no `<thead>` already. Because this runs
as part of sanitization, it covers every write of a `RichText` field that goes through StruoCMS's item
write pipeline — the editor, a direct API POST, a GraphQL mutation, an importer that calls the API —
not only content that passed through TipTap. From that point on, the stored HTML a frontend renders carries a real
`<thead>`, and `@tailwindcss/typography`'s `thead th` rules match it the way they are meant to.
Content saved before this normalizer existed keeps its old shape until it is next re-saved: there is
no backfill.

The editor still cannot display that `<thead>` — its schema has nowhere to put the node — so the admin
SPA carries its own CSS rule mirroring the plugin's header treatment onto the `tbody`-nested `th`
markup TipTap actually produces, scoped to the same first-row-all-`<th>` shape the server normalizes. The two
sides now arrive at the same appearance through two separate mechanisms instead of one: this is
consistency that is actively maintained, not a byproduct of the editor and the frontend rendering the
same stored string. A fork that substantially restyles its own tables will not see the editor's header
treatment follow along, because that CSS rule mirrors the typography plugin's own defaults, not
whatever a fork replaces them with.

One consequence follows from the "first row" condition being literal: the table right-click menu's
header-row toggle can also mark any other row as a header row, and a header row that is not a table's
first row is left inside `tbody` by the server. The normalizer deliberately never reorders a table's
content, so wrapping a mid-table row in `<thead>` is not an option — that is why the published output
still has no `<thead>` there, and it is why the editor's CSS, which only styles what the server also
wraps, leaves that row unstyled too. The editor's plain rendering of that row is not a bug; it is
accurate, because that is exactly how the row will render once published.

None of this makes the editor an exact preview of production rendering: a frontend's own
customization of Tailwind, of the typography plugin, or a rendering stack that uses neither, is
outside what this template controls.

### The link dialog and pre-existing links

The toolbar's and the bubble menu's Link button both open the same modal dialog
(`RichTextLinkDialog.vue`) instead of the browser's own `window.prompt`, driven by one shared `link`
command (`frontend/src/components/fields/richTextCommands.ts`) — there is exactly one dialog instance
per editor, reused by both entry points. Besides the URL field, the dialog adds an "open in new tab"
checkbox and, when the selection is already inside a link, a Remove button. Confirming the dialog sets
the link mark's `target` to `_blank` or clears it; the dialog itself never sets `rel` — chapter 5's
`RichText` contract table is what decides the stored `rel`, derived server-side from whatever `target`
value reaches the sanitizer.

That server-side derivation has a consequence for links stored before this dialog existed: they carry
`rel="noopener noreferrer"` and no `target` at all. Sanitization runs on every write of a `RichText`
field's full value, not only on the part a user actually touched, so the first time such an item is
saved again — even if nobody edits that particular link — the sanitizer sees no `target` on it and
applies the same rule as any other same-tab anchor, removing the `rel` it has no `target` to justify.
The link silently becomes `<a href="…">`. Because those links already opened in the same tab,
`noopener` was never doing anything for them; what actually changes is that they stop suppressing the
Referer header on that click. There is no backfill — the same position this chapter already takes for
`<thead>` above — so a fork with a large body of pre-existing links should expect this `rel` to
disappear gradually, one save at a time, not all at once.

### Resizing images in the editor

An inserted image in the `richText` field can be resized by dragging one of its eight handles — the
four corners plus the four edge midpoints — at any time while the field is editable; resizing has no
selection prerequisite. That behavior is not a bespoke node view StruoCMS wrote: it comes from
upstream, `@tiptap/extension-image`'s own `resize` option configured on the `Image` extension in
`RichTextInput.vue` (`resize: { enabled: true, minWidth: 40, alwaysPreserveAspectRatio: true,
directions: [...all eight] }`), which swaps in `@tiptap/core`'s `ResizableNodeView` for every image
node. `alwaysPreserveAspectRatio: true` locks every drag to the image's own ratio — upstream's own
default only does that while Shift is held — and it applies the same way to all eight handles: which
one is grabbed only decides which axis is primary for the ratio math, not whether the ratio holds.
It does not decide anything about position, either: `ResizableNodeView` never repositions the
element for any handle, so every drag grows or shrinks the image from its top-left corner — dragging
the left handle inward does not pin the right edge in place and grow leftward the way a design
tool's handles usually do. `minWidth` keeps a handle from shrinking the image into an unusably small
target.

What this repo supplies is the handles' appearance, not their behavior. `ResizableNodeView` attaches
and positions each handle unconditionally — absolute positioning plus a `data-resize-handle`
attribute — but sets no size, background, or cursor of its own, so without CSS every handle exists in
the DOM but is 0×0, invisible, and unclickable. `RichTextInput.vue`'s own `<style scoped>` block
supplies that styling, keyed off the `[data-resize-handle]` attribute selector (size, background
color, border radius, a cursor for each of the eight directions). Selecting the image adds nothing
to the handles themselves — they are just as visible and draggable unselected — beyond a separate
outline rule on `[data-resize-container].ProseMirror-selectednode`. A fork that wants different
handle styling edits those rules, in that file — there is no separate handle component to swap.

`ResizableNodeView` also wraps the `<img>` in two container `<div>`s (`[data-resize-container]` around
`[data-resize-wrapper]`, with the handle elements as siblings of the `<img>` inside the wrapper) to
host the handles and manage layout during a drag. This exists only inside the live ProseMirror view —
never in `getHTML()`'s output, and never in the HTML that reaches the sanitizer or gets stored. A
stored `RichText` value's `<img>` is never wrapped.

Dragging a handle also writes `height` onto the image node's own attributes, because upstream's
`onCommit` always writes both dimensions after a resize. `RichTextInput.vue` keeps that out of stored
HTML by overriding `height`'s `addAttributes()` with `rendered: false`, so `getHTML()` never
serializes it. This is not a duplicate of the sanitizer's own `height` strip (chapter 5): that guards
every input path against an attacker-controlled `height`, while this override exists so `getHTML()`'s
output already matches what the sanitizer would produce anyway — without it, the field's own
external-change comparison would see a phantom `height`-only difference on every update and reset the
cursor for no reason.

Right-clicking inside this field now opens one of two context menus depending on what was clicked. A
right-click landing directly on an `<img>` opens an image menu (edit alt text, delete image); a
right-click anywhere else inside a table opens the table's own right-click menu. The image check runs
first, so an image sitting inside a table cell still gets the image menu, not the cell's. Both menus
are the same `RichTextContextMenu.vue` component rendering a different action list — the image actions
themselves are declared in `richTextImageActions.ts`, mirroring `richTextTableActions.ts` for tables.

One more detail worth recording explicitly, because it looks like an oversight otherwise: TipTap's
table extension always renders a `<colgroup>` — its `renderHTML` is hard-coded to
`["table", attrs, colgroup, ["tbody", 0]]`, not conditional on anything — and `GanssHtmlSanitizer`
strips it every time, because `colgroup` was never added to the tag allowlist. The table extension is
configured with `resizable: false`, so this editor never lets a user set a per-column width in the
first place; the `<colgroup>` TipTap always emits carries no information under that configuration, so
losing it costs nothing. This is a deliberate acceptance, not a bug to fix by allowlisting
`colgroup` — doing so would only start storing width data this editor has no way to produce
meaningfully.

## Restyling a vendored `ui/` component

**`frontend/src/components/ui/` is vendored, read-only generated output — never edit it, and never
`:deep()` into it.** A re-theme happens one layer up, in one of three places:

- **The token layer** (`frontend/src/assets/tokens.css`), for anything already exposed as a semantic
  custom property — a color, a radius, a shadow. Every vendored component and Tailwind utility reads the
  same token, so one edit reaches every consumer at once.
- **The `class` prop, at the point of use**, for anything expressible as a Tailwind utility class. Every
  vendored `ui/` component that renders styled markup merges its `class` prop through `cn()`
  (`frontend/src/components/ui/input/Input.vue` shows the pattern), so passing utilities where the
  component is used restyles that one usage without touching `ui/` —
  `frontend/src/components/fields/FilePicker.vue` and `FilesField.vue` both do this, passing a `class`
  into `DialogScrollContent` that widens it to `min(78vw,1300px)` and narrows that to `95vw` below a
  960px viewport.
- **A wrapper component that sits outside `ui/`**, for anything neither of the above can express — a
  fixed width on one specific usage, extra spacing, a one-off layout tweak. A wrapper's own `<style scoped>`
  block is unlayered CSS, and because Tailwind v4 puts every utility class in `@layer utilities`,
  an unlayered declaration on the same element outranks a utility regardless of specificity (unless the
  utility carries `!important`, which inverts this) — this is what makes a wrapping component's scoped
  style the reliable place to put such an override. Vue's scope id lands only on a child component's root
  element, though, not on what it renders internally, so a wrapper's scoped style reaches the vendored
  component's root element — the same node the `class` prop above lands on — and stops there; reaching
  what the component renders inside that root still needs the `:deep()` this section already rules out.
  That leaves the token layer as the one path that actually reaches inside, because the inner nodes read
  those custom properties themselves.

Avoid reaching for `!important` here: it wins the immediate override but leaves the *next* override —
yours or a fork's — fighting the same battle one level worse.

## UI locales and i18n namespaces

The admin UI's own interface language (menu labels, buttons, toasts, validation messages) is entirely
separate from content languages (chapter 6's `languages` table / `Translatable` fields) — it never
touches the API. It is `vue-i18n` (`legacy: false`) bootstrapped in `frontend/src/i18n/index.ts`, with
two message catalogs registered under `frontend/src/locales/`: `en.ts` and `zh-TW.ts` (the default;
`fallbackLocale: 'en'`). Each catalog is a plain nested object — namespaces such as `common`, `nav`,
`dashboard`, `collectionList`, `itemForm`, `media`, `revisions`, `rbac`, `settings`, `fields` (itself
nesting a `richtext` sub-namespace for the TipTap toolbar) — and both files must declare the same keys;
`en` is the fallback source if a key is ever missing from another locale.

The active locale is a `UiLocale` (`'zh-TW' | 'en'`, `frontend/src/theme/resolveInitialUiLocale.ts`)
resolved before any store exists (`localStorage['struo.uiLocale']`, else the hardcoded default
`'zh-TW'`), then owned at runtime by the `uiLocaleStore` Pinia store: `set(locale)` updates
`i18n.global.locale.value`, sets `<html lang>`, and persists the choice back to `localStorage`.
`UiLanguageSwitcher.vue` is the only place that calls it, driven by the shadcn/reka-ui `Select`
(`@/components/ui/select`) whose two options read `t('lang.zh-TW')` / `t('lang.en')`.

**To add a new UI locale** (e.g. Japanese):

1. Add `frontend/src/locales/ja.ts` exporting the same key structure as `en.ts` — every namespace, every
   key. There is no automated completeness check; a missing key silently falls back to `en`'s value.
2. Register it in `frontend/src/i18n/index.ts`'s `messages` map: `messages: { 'zh-TW': zhTW, en, ja }`.
3. Widen `UiLocale` in `theme/resolveInitialUiLocale.ts` to `'zh-TW' | 'en' | 'ja'` and its validation
   check (`saved === 'zh-TW' || saved === 'en' || saved === 'ja'`).
4. Add the option to `UiLanguageSwitcher.vue`'s `options` array, and a `lang.ja` key to every locale
   file — each catalog names every locale, including itself, for the switcher's own labels.

## Adding a custom field editor

Chapter 5 already used `Color` as the running example of a field interface whose shipped editor
(`TextField` — "plain text input, no swatch picker") is intentionally minimal. Replacing it end-to-end
shows the full registration contract: register a component, receive the field's current value, emit a
change.

Every field editor component follows the same three props and one event (see
`frontend/src/components/fields/TextField.vue` / `BooleanField.vue`):

```ts
defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
```

`field` is the resolved `FieldMeta` from `frontend/src/types/schema.ts` (label, `maxLength`, `options`,
…), `modelValue` is the field's current value in form state, and `disabled` is passed down when the
field is read-only or the form is submitting. `FieldInput.vue` — the dispatcher every generated form
actually renders — looks the component up through the registry and forwards all three, so a new editor
never needs to know it is being rendered inside a generated form at all.

**1. Write the component** — `frontend/src/components/fields/ColorSwatchField.vue`:

```vue
<script setup lang="ts">
import { Input } from '@/components/ui/input'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

// The native color input works in "#rrggbb"; fall back to black while the field is still empty.
function onPick(e: Event): void {
  emit('update:modelValue', (e.target as HTMLInputElement).value)
}
</script>

<template>
  <div class="flex items-center gap-2">
    <input
      type="color"
      :value="(modelValue as string) || '#000000'"
      :disabled="disabled"
      class="border-input h-9 w-12 shrink-0 cursor-pointer rounded-md border p-0.5"
      @input="onPick"
    />
    <Input
      :model-value="(modelValue as string)"
      :disabled="disabled"
      :maxlength="field.maxLength ?? undefined"
      @update:model-value="(v) => emit('update:modelValue', v)"
    />
  </div>
</template>
```

**2. Register it** — in `frontend/src/lib/fieldTypes/registry.ts`, swap the `color` entry's component.
Everything else about that entry — `def({ … })`'s default value, parse and serialize behavior, and its
`asString` list-column formatter — already stays correct for a plain string field, so only the
component changes:

```ts
import ColorSwatchField from '../../components/fields/ColorSwatchField.vue'
// …
color: def({ component: ColorSwatchField, listColumn: asString }),
```

That single line is the entire registration: `FieldInput.vue` resolves
`getFieldType(field.interface).component` and renders it for every `Color`-interface field across
every collection, with no per-collection wiring.

`FieldInterface`/`ALL_FIELD_INTERFACES` in `frontend/src/lib/fieldTypes/types.ts` is a **closed union of
the 33 interfaces already documented in chapter 5** — its own comment says it "MUST be kept in sync"
with the backend enum, and it is not itself extensible from the frontend alone: adding a genuinely new
interface value (as opposed to swapping the editor for an existing one, as above) also means adding it
to the backend `FieldInterface` enum and everywhere `MetadataScanner` reasons about it, which is outside
the scope of admin-SPA-only customization.

## Branding: in-app site settings vs. `appsettings` defaults

Two independent layers set the product name and logo shown on the login page, the topbar
(`BrandMark.vue`) and the browser tab title:

- **`appsettings.json`'s `Branding:Name` / `Branding:LogoUrl`** (chapter 3) are deploy-time defaults,
  read once at startup.
- **The in-app editor** — `SettingsView.vue`, a super-admin-only "Site Settings → Branding" form — calls
  `PUT /api/settings/branding` (`frontend/src/api/settingsApi.ts`: `{ brandName, logoFileId }` →
  `{ brandName, brandLogoUrl }`) and is stored in the singleton `site_settings` database row.

Anonymous `GET /api/config` — fetched once at boot in `main.ts`, before the app mounts, so branding
never flashes — resolves the effective brand field-by-field: the saved `site_settings` value wins if
present, falling back to the `appsettings.json` default otherwise. `appConfigStore.brandName` /
`brandLogoUrl` hold whatever `/api/config` returned; `BrandMark.vue` renders the logo image if
`brandLogoUrl` is set, else a single-letter mark from `brandInitial`. `main.ts` also keeps the browser
tab title reactively in sync with `appConfig.brandName` (a `watch`, not a one-shot set), so an in-app
rename takes effect immediately without a reload — restarting the API process is only needed to change
the *deploy-time default*, per chapter 3.

## Dev proxy configuration and pointing the SPA at a different API

`frontend/vite.config.ts`'s dev server proxies same-origin `/api` requests to the API:

```ts
server: {
  port: 5173,
  host: '127.0.0.1',
  proxy: { '/api': { target: 'http://localhost:5221', changeOrigin: true } },
}
```

This is why chapter 2's walkthrough needs no CORS configuration at all: the browser only ever talks to
`http://localhost:5173`, and Vite forwards `/api/*` server-side to the API on `5221`. To point the dev
SPA at a different API instance — a different port, a remote dev box, a container — change `target`
here. There is no separate frontend-side base-URL setting for this same-origin mode:
`frontend/src/api/apiClient.ts` (and `filesApi.ts`/`richTextImages.ts`) always fall back to relative
`/api/...` paths and rely entirely on this proxy (or, in production, on the SPA being served from the
same origin as the API) to reach the backend.

### `VITE_API_BASE_URL`: true cross-origin (SPA and API on different origins)

The proxy above only works when the SPA and API are served from the same origin (directly, or via the
dev proxy standing in for one). For a deployment where the SPA is genuinely served from a different
origin than the API, set `VITE_API_BASE_URL` to the API's full origin (e.g. `https://api.example.com`)
in `frontend/.env` (copy the tracked `frontend/.env.example`, which documents the same default/override
split). `apiClient.ts`, `filesApi.ts` and `richTextImages.ts` each read
`import.meta.env.VITE_API_BASE_URL`, falling back to `/api` when it's unset — this is a Vite build-time
variable, so changing it needs a rebuild/restart of the dev server or a new production build, not just a
page reload.

This mode also requires a matching backend change: configure `Struo:Cors:AllowedOrigins` (e.g.
`Struo__Cors__AllowedOrigins__0=https://app.example.com`) to allow the SPA's origin
(`src/Struo.Api/Auth/CorsWiring.cs` reads this key; empty/absent means no origins are allowed).
Configuring any allowed origin also flips the authentication cookie from `SameSite=Lax` to
`SameSite=None` **and** forces `Secure` on (`src/Struo.Api/Auth/AuthWiring.cs`) — browsers only honor
`SameSite=None` over HTTPS, so both the SPA and the API must be served over HTTPS in this mode; it will
not work over plain HTTP.

## Next steps

- Chapter 4, [Defining a Collection](04-defining-a-collection.md), and chapter 5,
  [Field Types & Interfaces](05-field-types.md), for the metadata that drives most of the admin SPA
  without any code here.
- Chapter 6, [Internationalization](06-internationalization.md), for content languages, as distinct
  from the UI locale covered here.
- Chapter 15, [Deployment, Operations & Testing](15-deployment-operations-testing.md), for building and
  serving this SPA in production, and for its own test layer (`pnpm test`, `pnpm e2e`).
