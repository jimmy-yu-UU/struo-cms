# 14. Admin SPA Customization

The admin SPA (`frontend/`) is a Vue 3 + PrimeVue + Pinia application driven almost entirely by the
metadata the API exposes at `GET /api/schema`. This chapter is about the remaining part that is
genuinely code, not metadata: theming, i18n, field editors, branding, and how the dev server reaches
the API — and where in `frontend/src` each of those lives.

## When to customize vs. when metadata is enough

Most of what looks like "admin UI work" is not. Chapter 4 shows that adding a `[CmsCollection]` with
`[CmsField]`s is enough on its own to make a full collection — sidebar entry, paginated list with
schema-driven columns, and a create/edit form — appear with zero Vue code touched. Chapter 2's "no
Content group with zero collections" and chapter 4's "the sidebar now shows a Content group" describe
the same schema-driven rendering, before and after adding one. The admin SPA never hardcodes a
collection's fields, columns or labels; the schema endpoint is the only place that information comes
from.

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
| `assets/` | `theme.css` — the OKLch design-token custom properties and the shell/layout CSS built on them. |
| `components/` | `fields/` (one editor component per field interface, chapter 5), `common/` (`PageHeader`, `ListToolbar`, `TableFooter` — shared across every list/form view), `shell/` (topbar, sidebar nav item, theme toggle, UI language switcher, brand mark), `dashboard/`, `media/`, `revisions/`, `rbac/`. |
| `composables/` | Cross-cutting reactive logic, e.g. `useDashboardData.ts`. |
| `i18n/` | `index.ts` — the `vue-i18n` instance (`legacy: false`), wired to `locales/`. |
| `layouts/` | `AppShell.vue` — the topbar + sidebar + content grid every authenticated route renders inside. |
| `lib/` | Framework-free helper functions: `fieldTypes/` (the field-type registry, below), plus the formatting/validation/query helpers views and field components share. |
| `locales/` | `en.ts` / `zh-TW.ts` — the admin UI's own message catalogs, distinct from content languages (chapter 6). |
| `router/` | `index.ts` (routes), `guard.ts` (the auth/permission navigation guard). |
| `stores/` | Pinia stores: `authStore`, `appConfigStore`, `schemaStore`, `themeStore`, `uiLocaleStore`, `sidebarStore`, `languageStore`. |
| `theme/` | `preset.ts` (the custom PrimeVue Aura preset); `resolveInitialTheme.ts` / `resolveInitialUiLocale.ts` (first-paint `localStorage`/media-query resolution, read before any store exists). |
| `types/` | `schema.ts` — TypeScript mirrors of the backend DTOs (`FieldMeta`, `CollectionMeta`, `RelationMeta`, …). |
| `views/` | One component per route: `DashboardView`, `CollectionListView`, `ItemFormView`, `MediaLibraryView`, `SettingsView`, `LoginView`. |

## Design tokens and theming

Two layers cooperate, and both must change together for a re-theme to stay consistent:

1. **`frontend/src/assets/theme.css`** — plain CSS custom properties in OKLch (`--bg`, `--surface`,
   `--fg`, `--muted`, `--border`, `--accent`, plus `--success`/`--warn`/`--danger`, radii, shadows,
   `--sidebar-w`), declared once on `:root` for light and re-declared on `.app-dark` for dark. All of
   the shell/layout CSS in the same file (`.shell`, `.topbar`, `.sidebar`, `.nav-item`, …) reads these
   variables — it never hardcodes a color.
2. **`frontend/src/theme/preset.ts`** — a PrimeVue `definePreset(Aura, …)` (`StruoPreset`) that maps
   PrimeVue's own semantic tokens (`primary`, `surface`, and per-color-scheme `color`/`hoverColor`/
   `activeColor`) onto the **same** palette (`sky` for primary, `slate` for surface), so PrimeVue's own
   components (buttons, inputs, dialogs) match `theme.css`'s hand-styled shell instead of drifting from
   it — the file's own comment states this explicitly ("same palette as assets/theme.css so both layers
   flip together").

Both are registered once, in `frontend/src/main.ts`:

```ts
app.use(PrimeVue, { theme: { preset: StruoPreset, options: { darkModeSelector: '.app-dark' } } })
```

`darkModeSelector: '.app-dark'` is the link between the two layers: `themeStore.apply()` toggles the
`.app-dark` class on `<html>`, which simultaneously flips `theme.css`'s custom properties (a plain CSS
selector match) and PrimeVue's own dark-mode token set (its own `darkModeSelector` mechanism) — one
class, two systems, nothing separate to keep in sync. The initial mode is resolved before Pinia/Vue
even exist, in `theme/resolveInitialTheme.ts`: a saved `struo.theme` in `localStorage`, else
`prefers-color-scheme`, else `light`.

To re-theme: edit the `struoPresetConfig` semantic tokens in `preset.ts` (swap `sky`/`slate` for
different PrimeVue palette tokens, or hand-write OKLch values) and the corresponding custom properties
in `theme.css`'s `:root`/`.app-dark` blocks. Chapter 3's `Branding:Name`/`Branding:LogoUrl` reach only
the product name and logo, never the color palette — the palette is a template default edited in
source, not a per-deployment configuration key.

## Overriding PrimeVue's built-in styles

**Caution:** a same-specificity rule in `theme.css` does not reliably beat a PrimeVue component's own
runtime-injected styles. PrimeVue ships its component CSS as its own stylesheet, not as part of
`theme.css`'s cascade — a bare `.p-select { … }` in `theme.css` ties on specificity against PrimeVue's
own `.p-select` rule, and which one wins then depends on injection/source order, not intent. This has
already bitten this codebase once: the comment directly above `.topbar .lang-switcher { display: none; }`
in `theme.css` notes it needs "0,2,0 specificity: must beat PrimeVue's runtime-injected
`.p-select{display:inline-flex}`".

**The correct approach: raise specificity with a compound selector**, not a bare PrimeVue class. Two
patterns already used in this codebase:

- A plain compound selector in unscoped `theme.css` — `.topbar .lang-switcher` (two classes,
  specificity `0,2,0`) beats bare `.p-select` (`0,1,0`).
- A component-scoped `:deep()` paired with a real ancestor class inside a `<style scoped>` block —
  `frontend/src/components/ItemForm.vue`'s `.field :deep(.p-select), .field :deep(.p-multiselect),
  .field :deep(.p-treeselect) { width: 100%; max-width: 480px; }`, or `LoginView.vue`'s
  `.field :deep(.p-inputtext), .field :deep(.p-password) { … }`. `:deep()` alone does not raise
  specificity — pairing it with an ancestor class does.

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
`UiLanguageSwitcher.vue` is the only place that calls it, driven by a PrimeVue `Select` whose two
options read `t('lang.zh-TW')` / `t('lang.en')`.

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
import ColorPicker from 'primevue/colorpicker'
import InputText from 'primevue/inputtext'
import type { FieldMeta } from '../../types/schema'

defineProps<{ field: FieldMeta; modelValue: unknown; disabled?: boolean }>()
const emit = defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()

// PrimeVue's ColorPicker works in bare hex ("ff0000"); the stored/API value is "#ff0000".
function onPick(hex: string): void {
  emit('update:modelValue', `#${hex}`)
}
</script>

<template>
  <div class="color-swatch-field">
    <ColorPicker
      :model-value="(modelValue as string)?.replace(/^#/, '') ?? ''"
      :disabled="disabled"
      @update:model-value="onPick"
    />
    <InputText
      :model-value="(modelValue as string)"
      :disabled="disabled"
      :maxlength="field.maxLength ?? undefined"
      @update:model-value="(v) => emit('update:modelValue', v)"
    />
  </div>
</template>

<style scoped>
.color-swatch-field { display: flex; align-items: center; gap: 8px; }
</style>
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
  proxy: { '/api': { target: 'http://localhost:5221', changeOrigin: true } },
}
```

This is why chapter 2's walkthrough needs no CORS configuration at all: the browser only ever talks to
`http://localhost:5173`, and Vite forwards `/api/*` server-side to the API on `5221`. To point the dev
SPA at a different API instance — a different port, a remote dev box, a container — change `target`
here. There is no separate frontend-side base-URL setting: `frontend/src/api/apiClient.ts` always calls
relative `/api/...` paths and relies entirely on this proxy (or, in production, on the SPA being served
from the same origin as the API) to reach the backend.

## Next steps

- Chapter 4, [Defining a Collection](04-defining-a-collection.md), and chapter 5,
  [Field Types & Interfaces](05-field-types.md), for the metadata that drives most of the admin SPA
  without any code here.
- Chapter 6, [Internationalization](06-internationalization.md), for content languages, as distinct
  from the UI locale covered here.
- Chapter 15, [Deployment, Operations & Testing](15-deployment-operations-testing.md), for building and
  serving this SPA in production, and for its own test layer (`pnpm test`, `pnpm e2e`).
