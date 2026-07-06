# Phase 7g.6 — Frontend field-type registry + lazy i18n tabs (design)

**Date:** 2026-07-06
**Status:** approved (brainstorm), pending implementation plan
**Scope:** a small inserted **pure-refactor** slice on the Vue 3 admin SPA, done **before** the 7g+
field-type work per the architecture audit (F1 + F3, "做 7g+ 之前"). It introduces a single
field-type **registry** so that adding a field type stops being shotgun surgery, folds the
empty-`Guid?` coercion into that registry (F2), and makes the i18n locale tabs lazy (F3).
**Zero behaviour change. No backend change, no DDL, no live gate.** No new field types are
implemented — `Json`/`KeyValue`/`Repeater`/`Files`/`MultiSelect`/`CheckboxGroup`/`Tags`/`Hidden`/
`Uuid` continue to render read-only exactly as today.

---

## 1. Goal & positioning

Today, per-field-type behaviour is scattered across **six** places with no compile-time
coverage check (audit F1):

| Concern | File |
|---|---|
| component dispatch (v-if chain) | `components/fields/FieldInput.vue` |
| interface → coarse input "kind" | `lib/fieldInputKind.ts` |
| deserialize + default value | `lib/parseItemToForm.ts` |
| serialize + type coercion | `lib/buildItemPayload.ts` |
| list-column eligibility | `lib/selectListColumns.ts` (`SCALAR_INTERFACES` set) |
| list cell rendering | `lib/formatCell.ts` |

Adding one field type means editing a union, a map, a template, parse/serialize, and two list
helpers, with **nothing forcing you to cover every case** — the exact trap behind the recurring
empty-`Guid?` → Postgres 500 bug (7c/7d/7e). This slice replaces the scatter with a single registry
keyed by `FieldInterface`, so a new field type becomes **one registry entry + one small component +
its own tests**, and the TypeScript compiler forces coverage.

This is a **refactor**: the existing 177 frontend tests (parse/serialize/validate/list helpers +
component tests) are the safety net — staying green proves no regression.

## 2. Approach (decided)

**Uniform field-component contract + registry keyed by `FieldInterface`** (brainstorm option A).

Rejected alternatives: a `props(field)` mapper without wrapper components (props logic stays
centralised, `radio`/`file` need custom templates not a single PrimeVue component, and a field type
can't be unit-tested in isolation); a thin lookup layer that leaves the helpers hard-coded (does not
actually remove the shotgun surgery).

## 3. Module layout

```
frontend/src/lib/fieldTypes/
  types.ts      # FieldInterface string-union (all backend camelCase interfaces)
                # + FieldTypeDef interface + shared helpers (isEmpty, coerceEmptyToNull)
  registry.ts   # Record<FieldInterface, FieldTypeDef> + getFieldType(iface): FieldTypeDef
  defs.ts       # the per-interface definitions (wires components + parse/serialize/listColumn)
frontend/src/components/fields/
  TextField.vue        # text, slug, email, url, color, phone, password  (InputText + maxlength)
  TextareaField.vue    # textarea, markdown, code                        (Textarea + rows + maxlength)
  NumberField.vue      # number, slider, rating                          (InputNumber)
  BooleanField.vue     # boolean, checkbox                               (Checkbox binary)
  DateField.vue        # date, time, dateTime                            (DatePicker; mode from interface)
  SelectField.vue      # select                                          (Select)
  RadioField.vue       # radio                                           (RadioButton group)
  DividerField.vue     # divider                                         (<hr>)
  ReadonlyField.vue    # fallback + all not-yet-implemented interfaces
  RichTextField.vue    # thin wrapper adapting existing RichTextInput.vue to the uniform contract
  FileField.vue        # thin wrapper adapting existing FilePicker.vue (file/image); owns ''→null
  FieldInput.vue       # slimmed to <component :is="getFieldType(field.interface).component" ...>
```

`lib/fieldInputKind.ts` is **retired** (its role is subsumed by the registry). Relations
(`relationInputKind.ts` / `RelationInput.vue`) are a **separate** system (not `FieldInterface`) and
are **not touched**.

### Uniform component contract

Every `*Field.vue` takes exactly:

```ts
defineProps<{ field: FieldMeta; modelValue: unknown; disabled: boolean }>()
defineEmits<{ (e: 'update:modelValue', v: unknown): void }>()
```

Per-type props that used to live in the dispatcher move **inside** each component: `TextField`
reads `field.maxLength`; `DateField` derives `timeOnly`/`showTime` from `field.interface`;
`SelectField`/`RadioField` read `field.options`; `FileField` derives `image` from
`field.interface === 'image'`. `disabled` at the call site already OR-folds `field.readOnly`
(kept as-is).

## 4. FieldTypeDef contract

```ts
export interface FieldTypeDef {
  component: Component
  // default form-model value for this field (file/image -> null, everything else -> '')
  defaultValue(field: FieldMeta): unknown
  // raw API value -> form-model value  (raw ?? defaultValue(field))
  parse(raw: unknown, field: FieldMeta): unknown
  // form-model value -> API payload value; single home for per-type coercion
  // (FileField: '' -> null, so a nullable Guid FK never serialises "" -> Postgres 22P02/500)
  serialize(value: unknown, field: FieldMeta): unknown
  // list-column support: null = not eligible as a table column;
  // otherwise how to render the cell (non-empty value; the '—' guard stays in formatCell)
  listColumn: { format(value: unknown, field: FieldMeta): string } | null
  // optional per-type validation beyond generic required/maxLength.
  // Seam only — no built-in interface populates it in this slice (added when Json etc. needs it).
  validate?(value: unknown, field: FieldMeta): string | null
}
```

### Faithful (zero-change) semantics

To make this a true no-behaviour-change refactor, the defs reproduce today's behaviour exactly:

- **`defaultValue`**: `file`/`image` → `null`; every other interface → `''`.
- **`parse`**: `raw ?? defaultValue(field)`.
- **`serialize`**: identity, except `FileField` maps `''` → `null` (the F2 coercion, previously
  duplicated as string comparisons in `buildItemPayload.ts:27` and `parseItemToForm.ts:16`).
- **`listColumn`**: non-`null` for exactly today's `SCALAR_INTERFACES`
  (`text, textarea, slug, email, url, phone, color, number, slider, rating, boolean, checkbox, date,
  time, dateTime, select, radio`), each with a `format` matching `formatCell`'s current output
  (`select`/`radio` → option label; `boolean`/`checkbox` → `Yes`/`No`; `date`/`time`/`dateTime` →
  `toLocaleString`; otherwise `String(value)`). All other interfaces → `listColumn: null`.

### Exhaustiveness

`registry` is typed `Record<FieldInterface, FieldTypeDef>`, so a missing interface is a **compile
error** (`vue-tsc`). `getFieldType(iface: string)` returns `registry[iface] ?? readonlyDef`, so
unknown interfaces (a base-template consumer's custom interface) still degrade gracefully to
read-only — the forward-compatibility the audit praised. A runtime test asserts every known
interface resolves to a def and an unknown string resolves to the readonly def; the `FieldInterface`
union is documented as the mirror of the backend `FieldInterface` enum (must be updated together).

## 5. Refactoring the six call sites to iterate the registry

- **`FieldInput.vue`** → `<component :is="getFieldType(field.interface).component" :field :model-value :disabled @update:model-value>`.
- **`parseItemToForm.ts`** → each shared/translatable field value via `def.parse(raw, f)`; the
  shared/translatable/relations **structure** and the relation handling are unchanged.
- **`buildItemPayload.ts`** → each field value via `def.serialize(v, f)`; the structural logic
  (create-mode `isEmpty` omission, update sends filled locales, `version` echo, relation FK/M2M
  writing) is unchanged. The `file`/`image` string comparisons are **removed** here (now in
  `FileField`'s def).
- **`selectListColumns.ts`** → `eligible = fields.filter(f => !f.isSystem && !f.hidden && getFieldType(f.interface).listColumn !== null)`; ordering (`defaultDisplayField` first) and `MAX_COLUMNS` cap unchanged.
- **`formatCell.ts`** → keep the top-level `null/undefined/'' → '—'` guard, then delegate to
  `getFieldType(field.interface).listColumn?.format(value, field) ?? String(value)`.

## 6. Lazy i18n tabs (F3)

`ItemForm.vue` currently mounts **every** locale's `TabPanel` body at once (`ItemForm.vue:62-69`),
so N locales × M translatable fields instantiate simultaneously — with translatable `RichText`
fields that is N TipTap/ProseMirror editors mounted at once.

Fix: render only the active locale's panel body (`v-if="loc.code === activeLocale"` inside the
`TabPanel`). The data lives in `model.translations[loc.code]` (the source of truth), so switching
tabs **preserves entered values**; only the currently-visible locale's `FieldInput`s are mounted.
The `TabList` (tab headers) stays fully rendered. Add `watch(() => props.errors, ...)`: when
validation errors appear, set `activeLocale = defaultCode` so the default-locale errors (the only
translatable errors surfaced, `ItemForm.vue:67`) are always visible — a strict improvement over
today, where they can sit in a hidden-but-mounted panel.

## 7. Testing & verification

**Safety net — all existing tests stay green** (characterization): `parseItemToForm.test.ts`,
`buildItemPayload.test.ts`, `validateItem.test.ts`, `selectListColumns.test.ts`,
`formatCell.test.ts`, and the component tests. `fieldInputKind.test.ts` is replaced by registry
tests (the module is retired).

**New tests:**

- **registry** (`fieldTypes`): every known interface resolves to a def; unknown → readonly def;
  `parse`/`serialize`/`defaultValue` round-trip for representative interfaces (incl. `file`/`image`
  `''`→`null`); `listColumn` eligibility set equals the old `SCALAR_INTERFACES`; `format` output
  matches the old `formatCell` per interface.
- **each new `*Field.vue`**: renders the correct control for its interface(s), emits
  `update:modelValue`, honours `disabled`, binds `maxlength` where relevant (`Text`/`Textarea`).
- **`ItemForm` lazy tabs**: only the active locale's translatable fields are in the DOM; switching
  locale preserves model values; validation errors move the active tab to the default locale.

**Gates:** `pnpm test` all green (177 existing + new), `pnpm vue-tsc` typecheck clean (this is what
enforces registry exhaustiveness), `pnpm build` clean. **No backend build/test change and no live
gate** — this slice touches no server code and no persistence.

## 8. Scope boundary / non-goals

- **No new field types.** `Json`, `KeyValue`, `Repeater`, `Files`, `MultiSelect`, `CheckboxGroup`,
  `Tags`, `Hidden`, `Uuid` all map to `ReadonlyField` — identical to today's behaviour. Implementing
  them is the subsequent 7g+ slices, each of which then adds exactly one registry entry (+ component
  + tests) and its own persistence design + live gate.
- **No backend, no DDL, no live gate.** The relations system (`relationInputKind`/`RelationInput`)
  is untouched.
- **F6** (`itemsApi` `res.meta?.total` fallback) and **F8** (shared media-browser composable) are
  out of scope (F8 belongs with the later `Files` slice).
- **`validateItem` stays generic** (required + `maxLength`); the registry only *reserves* the
  `validate?` seam — no built-in interface populates it in this slice.
