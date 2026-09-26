# 19. Admin Customization

The admin UI is a Vue SPA, and most customization needs are met by metadata alone. This chapter
covers what's left — which file under `frontend/src` to change, and what that costs.

## Ask whether metadata is enough

A class carrying `[CmsCollection]` and `[CmsField]` alone produces a sidebar entry, a schema-driven
paginated list, and a create/edit form — without touching a line of Vue. Every field, list column,
and label in the admin UI comes from `GET /api/schema`; there's no second source.

The display name, `Icon` (valid values are the keys of `ICON_MAP`; a name the table doesn't have
always falls back to a generic file icon — see
[Chapter 5: Defining Collections](05-collections.md)), field interfaces (see
[Chapter 6: Field Types and Editors](06-field-types.md)), and `Hidden` are all metadata this schema
governs — change the metadata and the change follows, with no need to touch `frontend/src`.

`frontend/src` is for what metadata can't express: a field needs an editor no built-in interface
provides; branding needs to change more than a name and a logo; the admin UI itself needs to speak
another human language; or an entire workflow doesn't fit the generic list/form template at all.

Beyond that — adding a collection, a field, a relation, validation, permissions — belongs to
[Chapter 5](05-collections.md), [Chapter 6](06-field-types.md),
[Chapter 8: Relations](08-relations.md), and
[Chapter 17: Roles and Permissions](17-roles-and-permissions.md); none of it changes a line of the
admin SPA.

## The `frontend/src` map

Two files sit at the root, `App.vue` and `main.ts`; everything else is a subdirectory. Each module
usually keeps a same-named `.test.ts` beside it — the test file lives next to the module.

- `api/`: one module per REST resource, plus a shared fetch wrapper that understands the envelope
  format.
- `assets/`: the source of the admin UI's entire look — `tokens.css` and `theme.css`, covered in
  the next section.
- `components/`: `ItemForm.vue` sits at this level; everything else is a subdirectory by purpose,
  including the vendored `ui/` (see "Restyling a vendored `ui/` component").
- `composables/`: logic shared across components — `useConfirm.ts`, `useToast.ts`.
- `i18n/`: the `vue-i18n` instance, wired to `locales/`.
- `layouts/`: `AppShell.vue`, the top bar/sidebar/content layout shared by every signed-in
  route.
- `lib/`: helpers with no Vue dependency, including the field-interface registry under
  `fieldTypes/`.
- `locales/`: the admin UI's own message catalogs, `en.ts` and `zh-TW.ts` — a separate concern
  from content locales.
- `router/`: the route table, navigation guards for auth and permissions, a recovery routine that
  reloads the page on a chunk-404, and the route meta type declarations.
- `stores/`: Pinia stores — `authStore`, `appConfigStore`, `schemaStore`, `themeStore`,
  `uiLocaleStore`, `languageStore`, `confirmStore`.
- `theme/`: the theme and UI language decided before the first render, reading `localStorage` and
  media queries — before Pinia or Vue is even running.
- `types/`: TypeScript counterparts to backend DTOs, plus form-component-specific types.
- `views/`: one component per route — dashboard, collection list, form, media library, settings,
  login.

The sections below go deeper into `assets/` (design tokens), the icon table in `lib/`,
`components/ui/`, the rich-text editor under `components/fields/`, and `locales/`; this chapter
doesn't expand on the rest.

## Design tokens and the theme

`tokens.css` and `theme.css` work together, switching between light and dark via the same
`.app-dark` class on the root element — there's no second place to keep in sync.

`tokens.css` is Tailwind's entry point and also shadcn's semantic token layer (`--background`,
`--foreground`, `--primary`, `--radius`, the sidebar's own tokens, and more), declared once under
`:root` and again, as the dark variant, under `.app-dark`; a `@theme inline` block maps every token
to a Tailwind utility class (`bg-background`, `text-primary`, and so on), and the vendored `ui/`
components and first-party components read the same mapping.

`tokens.css` also repoints shadcn's own `.dark` dark-mode convention to this project's `.app-dark`,
rather than renaming this project's class to match shadcn's; both the e2e tests and the script that
paints the page once before Vue mounts depend tightly on this exact class name, so it can't be
renamed casually.

`theme.css` is global style with no Tailwind layer at all: custom properties for
page/surface/foreground, status, shadow, overlay and font, which first-party scoped CSS reads, plus
the `html`/`body` reset and the theme-switch transition rule.

Because none of these rules sit in any layer, an unlayered declaration always beats a layered one on
the same element, whatever the utility class's own specificity — unless the layered side carries its
own `!important`, which flips the outcome. A hand-written CSS override that quietly does nothing is,
nine times out of ten, this rule at work.

Both stylesheets are imported once, in `main.ts`, the SPA's entry point — `tokens.css` first, and a
third line imports the toast library's own stylesheet (that package doesn't import itself). The
theme store toggles `.app-dark` on the root element, flipping both files' custom properties in one
step with nothing to notify, and persists the choice; the initial mode is decided before Pinia or
Vue runs — `localStorage`'s `struo.theme` first, then `prefers-color-scheme`, and light otherwise.

To change the color scheme, edit the semantic custom properties under `tokens.css`'s `:root` and
`.app-dark` (both the vendored components and the utility classes read this contract); when the
change touches a first-party shell piece like the sidebar or breadcrumbs, the matching property in
`theme.css` needs the same edit.

`Branding:Name` and `Branding:LogoUrl` only change the brand name and logo, never the color scheme —
the palette is a source-code default, not a per-deployment setting key. In dark mode, cards and the
page background converge to the same value, so the surface color one step further down from the card
has its own token in `tokens.css`, rather than being computed from `theme.css`.

## Adding a sidebar icon

`ICON_MAP` maps a fixed set of keys to Lucide components; a collection's declared `Icon` value goes
through `resolveIcon`, which accepts the key itself or a legacy `pi …`/`pi-…` class string, picking
the real token out of it. An unknown name always falls back to the generic file icon, without
breaking the sidebar. The keys are grouped by purpose:

- **Navigation and shell:** `th-large`, `images`, `cog`, `bars`, `user`, `sign-out`, `sun`,
  `moon`, `angle-down`, `angle-left`, `angle-right`, `chevron-left`
- **Actions:** `plus`, `search`, `pencil`, `eye`, `trash`, `undo`, `check`, `times`, `upload`,
  `copy`, `history`, `replay`
- **Sorting and alignment:** `arrow-up`, `arrow-down`, `align-left`, `align-center`,
  `align-right`, `align-justify`
- **Rich-text lists and tables:** `list`, `sort-numeric-down`, `table`
- **File and media kinds:** `file`, `file-edit`, `file-pdf`, `file-word`, `file-excel`, `image`,
  `video`, `volume-up`, `folder`, `folder-plus`
- **Semantic names the `Icon` property itself hands out:** `article`, `tag`, `megaphone`

Adding a sidebar icon: import the icon component in `frontend/src/lib/icons.ts`, add a key to
`ICON_MAP`, and put that key into the collection's `[CmsCollection(Icon = ...)]`. This table isn't
freely extensible — a coverage test checks, key by key, whether it is actually used by some token in
the source, by a semantic name an `Icon` property hands out, or by a class assembled at runtime.
Adding a key nobody uses turns the admin SPA's test suite red.

## Restyling a vendored `ui/` component

`frontend/src/components/ui/` is vendored, generated, read-only output — 33 component directories,
`button`, `dialog`, `table`, `select`, and `sidebar` among them.

Don't edit the files inside `ui/`, and don't reach in with a deep selector: Vue's scope id lands
only on a child component's **root** element, never on the nodes it renders internally, so a deep
selector never reaches them; the one path that does is the token layer, because those internal
nodes are already reading the same custom properties.

Recoloring always moves one layer outward, in this order: the token layer, for anything already
exposed as a semantic custom property, which changes every place that uses it in one move; the
`class` prop at the call site, for anything expressible as a utility class; or a wrapping component
of your own, for whatever neither of the first two can express.

Every vendored component that renders styling merges its own `class` prop into a shared class
helper, so passing extra utility classes at the call site changes only that one usage, without
touching `ui/`. A wrapper component's own scoped style is likewise unlayered, which makes it a
reliable place to override (see "Design tokens and the theme" for why).

Avoid `!important`: it wins this one override, but leaves the next one — yours or a fork's —
fighting the same battle from a worse position.

## The rich-text editor

The `richText` field's interactive surfaces each own one job: the toolbar, the always-visible full
command set; the bubble menu, inline formatting when text is selected; the context menu, structural
actions on a table or image; the node view, an object's own direct manipulation handles; and the
slash menu, keyboard-driven block insertion. What customization actually reaches is the command
registry, the link dialog, image sizing, and typography.

Commands themselves live in one registry: an array in `richTextCommands.ts`, each entry
`{ id, labelKey, group, icon, glyph, glyphTag, isActive, run }`. `group` is one of `inline`,
`align`, `block`, `insert`, `history`.

The toolbar renders the whole registry as buttons, plus three controls that don't fit that shape
(heading level, color, insert-table) and stay outside it; the bubble menu picks only the `inline`
group, and appears only when text is selected. Adding a `group: 'block'` or `group: 'insert'` entry
to the registry reaches both the toolbar and the slash menu together, with no per-surface wiring
needed; the bubble menu takes only `inline`, and never sees either of those two groups.

The slash menu's list is four segments in a fixed order: heading levels (`HEADING_LEVELS` in
`richTextHeadings.ts`, H2 through H6), the registry's `group: 'block'` entries, one hand-written
table entry, and finally the `group: 'insert'` entries. The toolbar's own table control inserts a
size the user picks, independent of the registry — its shape doesn't fit a single command; the
hand-written slash entry always inserts a fixed 3×3 table with a header row.

Aliases must be lowercase ASCII: the query string is lowercased before matching, but an alias is
compared as written, so an alias with any uppercase letter never matches.

A hand-written entry carries two extra obligations. If its `labelKey` doesn't appear in `EN_LABELS`,
the failure doesn't surface at load time — the slash menu throws as soon as it opens, because
`EN_LABELS` is assembled from the heading levels, the registry, and the table entry at module load,
and the candidate list is recomputed on every keystroke.

Its English label (`enLabel`) also has to be computed through the shared resolver, not written as
`t(key)` the way the display `label` is — that compiles, but ties the English name to the current UI
language, and any match keyed on the English name quietly stops working once the interface leaves
English. `richTextSlashExtension.ts` owns when `/` opens and how the keyboard drives it.

The link dialog (`RichTextLinkDialog.vue`) is one popup shared by the toolbar and bubble-menu link
buttons — not a native browser prompt — both triggered by the same `link` command. The user fills in
a URL and an "open in a new tab" checkbox; when the selection is already inside a link, a remove
button appears too. The dialog decides only `target`; the sanitizer derives `rel` from `target`
server-side, and the rule is in [Chapter 6](06-field-types.md).

Resizing an image is two steps: click once to select it, then drag one of eight handles (the four
corners plus the four edge midpoints), which appear only while this image is selected. The drag
behavior itself — minimum width, eight directions, aspect-ratio lock — is the upstream image
extension's own setting; what's added here is when the handles appear and what they look like,
written in `RichTextInput.vue`'s own scoped style.

The handles are 10px-square drag targets, smaller than the 24×24 WCAG 2.2 recommends — enlarging the
target means editing this same block. A single drag wants to write both width and height, but the
field overrides the `height` attribute to not serialize, so the `<img>` that ends up stored carries
only a width; height is derived back from that width's ratio.

The editing surface itself carries `class="prose dark:prose-invert"`, reading the typography
plugin's default style directly, so editing stays as close as possible to how the piece looks once
published; changing that look means editing the `--tw-prose-*` custom properties where the plugin is
registered in `tokens.css`, and writing them as unlayered rules — the same "unlayered beats layered"
rule from the design-tokens section above.

The table header row is the one gap the server fills in: the editor's schema has no `thead` node, so
the sanitizer, on the way out, wraps a table's first row in `<thead>` whenever every cell in it is a
`<th>` and there's no `<thead>` yet. This runs on every `RichText` write, not only on content from
the editor, and it isn't backfilled — content already stored waits for its next save to pick it up.

The check looks only at a table's first row, but the editor's table context menu can mark any row as
a header row; a header row marked anywhere but the first stays sitting in `<tbody>` once stored,
because the sanitizer never moves table content, and the editor's own header-mimicking CSS covers
only the first row too — that row looks like an ordinary row inside the editor, which is genuinely
how it looks once published, not an editor rendering mistake.

Two places here look like flipping one option, but actually swallow whole chunks of data, because
the sanitizer drops a non-allowlisted tag with its whole subtree. The table extension has a
"generate a wrapping `<div>`" option; turn it on and every table is deleted whole on save, not
unwrapped — leave this option off unless `div` is added to the sanitizer's allowlist first.

Adding H1 to the slash menu is the same trap: the allowlist starts at `h2`, since the page title is
already an H1, so an `<h1>` is stripped with its text. Actually adding it means editing the
sanitizer's allowlist first, widening the heading-level type, and adding one label key to each
locale file.

Which file to open for what:

- Commands and the toolbar/bubble menu: `richTextCommands.ts`
- Slash menu entries and aliases: `richTextSlashCommands.ts`
- Heading levels: `richTextHeadings.ts`
- Link dialog: `RichTextLinkDialog.vue`
- Table and image context-menu actions:
  `richTextTableActions.ts`/`richTextImageActions.ts`
- Image resize behavior and handle style: `RichTextInput.vue`
- Typography: `tokens.css`

Every file except `tokens.css` is under `frontend/src/components/fields/`.

## UI language

The admin UI's own language is independent of content locales; the chosen locale is never sent to
the API, while the set on offer comes from `GET /api/config` —
see [Chapter 7: Multilingual Content](07-i18n.md). It runs `vue-i18n`'s non-legacy mode; the
message catalogs are registered in one place, the `catalogs` object in `src/locales/index.ts`, and two
ship: `zh-TW.ts` and `en.ts`, each a nested object with top-level namespaces by screen (`nav`,
`login`, `itemForm`, `settings`, and so on) and a `richtext` sub-namespace under `fields`. The
`UiLocale` type, the i18n message table, the switcher's options and the key-symmetry test are all
derived from that registry.

Which catalogs are offered and which is the default come from `AdminUi:Locales` and
`AdminUi:DefaultLocale` (see [Chapter 4: Configuration Reference](04-configuration.md)), published by
`GET /api/config`; the SPA keeps only codes present in the registry. The language in effect is decided
after `/api/config` resolves and before the app mounts: `struo.uiLocale` from `localStorage` when it
is in the enabled set, otherwise the default; the i18n fallback points at the default too. A Pinia
store's `set()` switches i18n, updates `<html>`'s `lang` attribute, persists the choice, and refuses a
value outside the enabled set. `UiLanguageSwitcher`, in the app shell, is the only component that
calls it; its options are the enabled set, and with a single locale the component is not rendered.

Adding an interface language:

1. Add a catalog file with the same key structure as the existing ones.
2. Register it in `catalogs` in `src/locales/index.ts`.
3. Add a `lang.<code>` key (the switcher's option text) to every catalog.
4. Add the code to `AdminUi:Locales`, and to `AdminUi:DefaultLocale` if it should be the default.

One unit test compares every registered catalog's key set with the others; a key added to one but
not the others fails the admin SPA's test suite. A missing key at runtime falls back to the default
locale's catalog rather than breaking outright.

## Branding

Brand name and logo have two layers: the value edited in site settings wins, and
`Branding:Name`/`Branding:LogoUrl` are only the deployment-time defaults on the configuration side —
the keys are in the `Branding` section of [Chapter 4: Configuration Reference](04-configuration.md).

The in-app editor is a form under Site Settings visible only to a super-admin, submitting the brand
name and the logo's file id, and getting back the name and a resolved logo URL. The anonymous
`GET /api/config` computes this effective set field by field, cached server-side for 30 seconds and
evicted immediately on save, so a deliberate change is never held back by the TTL.

Saving blocks a logo file that isn't published; the config endpoint re-checks that file's published
status every time it recomputes the effective value, so a file later unpublished or trashed makes
the endpoint fall back to the deployment-time default rather than returning a dead URL.

The admin SPA fetches this config once before it mounts and stores it; the brand mark shows the logo
image when a URL resolves, and a single-letter mark otherwise. A watcher keeps the browser tab title
following the brand name continuously, not set once — renaming in-app takes effect immediately, and
restarting the API is needed only to change the deployment-time default.

## Custom field editors

To swap the field editor component an interface uses by default, change the component on its entry
in `frontend/src/lib/fieldTypes/registry.ts`; everything else stays correct as-is. The full
walkthrough is in [Chapter 6](06-field-types.md).

## The dev proxy and `VITE_API_BASE_URL`

During development, `frontend/vite.config.ts`'s `server` block (excerpt) decides how the dev server
runs:

```ts
  server: {
    port: 5173,
    host: '127.0.0.1',
    proxy: { '/api': { target: 'http://localhost:5221', changeOrigin: true } },
  },
```

`host` pins IPv4 loopback because, on some Windows setups, `localhost` resolves to IPv6 loopback
first and Vite then binds only `[::1]` — without pinning it, a browser hitting
`http://localhost:5173` can't connect at all. `proxy` forwards the same-origin `/api` to the API on
`:5221`, which is also why [Chapter 3: Getting Started](03-getting-started.md)'s walkthrough needs
no separate CORS configuration at all — the browser sees only Vite as its one origin.

Pointing the dev SPA at a different API instance is just a matter of changing `target` here; this
same-origin pattern has no admin-SPA-specific base-URL setting.

`VITE_API_BASE_URL` is needed only when the SPA runs on a different origin from the API; set it in
`frontend/.env` (copied from the tracked `frontend/.env.example`) to the API's full origin. This
variable is read in exactly three places — `apiClient.ts`, `filesApi.ts`, `richTextImages.ts` — each
falling back to the relative path `/api` when it isn't set. It's a Vite build-time variable;
changing it needs a dev-server restart or a rebuild, and a plain page refresh doesn't pick it up.

This pattern also needs the backend's cooperation: `Struo:Cors:AllowedOrigins` has to list the SPA's
origin. Setting it changes the session-cookie and HTTPS requirements — both sides then have to run
over HTTPS — see [Chapter 4](04-configuration.md).

## What's next

That covers admin customization; the next step is deploying the whole system to production — see
[Chapter 20: Deployment](20-deployment.md).
