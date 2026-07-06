# Phase 7g — Advanced Rich Text Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the TipTap editor and the server-side sanitizer allowlist in lockstep with basic tables, text alignment (left/center/right/justify), text colour (palette + free picker), and sub/superscript.

**Architecture:** The only backend change is the `GanssHtmlSanitizer` constructor allowlist (tags + `style` attribute + exactly two CSS properties: `color`, `text-align`). The frontend adds five TipTap extension wirings to `RichTextInput.vue`, six simple toolbar toggles inline, and two new presentational menu components (`RichTextColorMenu`, `RichTextTableMenu`) that emit events; the parent runs the editor commands. The field contract stays a plain HTML string — no save-path, dispatch, or `ItemService` change.

**Tech Stack:** .NET 10 / Ganss.Xss (`HtmlSanitizer` package, already installed) · Vue 3 + TipTap v3.27 (pnpm) · xUnit + AwesomeAssertions · Vitest + @vue/test-utils.

**Spec:** `docs/superpowers/specs/2026-07-06-phase7g-advanced-richtext-design.md`

## Global Constraints

- Branch: `phase7g-advanced-richtext` off `main` (isolated workspace via superpowers:using-git-worktrees at execution start).
- Package versions are NEVER hand-authored (CLAUDE.md §17.5): frontend deps via `pnpm add <pkg>`; no backend package changes needed.
- TDD: failing test first for every behaviour change (CLAUDE.md §17.2).
- Sanitizer allowlist must mirror editor output exactly — every capability lands as editor-change + sanitizer-change + test triplet (7f lesson: mismatch = silent data loss).
- Deliberately NOT allowed in sanitizer: `colspan`, `rowspan`, `colwidth`, `col`, `colgroup`, any CSS property other than `color`/`text-align`.
- Ganss/AngleSharp normalizes CSS serialization (e.g. `#e11d48` may re-serialize as `rgba(225, 29, 72, 1)`) — backend tests assert on property names / value survival, never on exact hex round-trip.
- Working directory for backend commands: repo root. For frontend commands: `frontend/`.
- Backend gate: `dotnet build` clean (warnings-as-errors) + `dotnet test` all green (316 baseline). Frontend gate: `pnpm test` all green (161 baseline) + `pnpm build` succeeds.

---

### Task 1: Backend — sanitizer allowlist extension

**Files:**
- Modify: `tests/Struo.Tests/Security/GanssHtmlSanitizerTests.cs`
- Modify: `src/Struo.Infrastructure/Security/GanssHtmlSanitizer.cs`

**Interfaces:**
- Consumes: existing `GanssHtmlSanitizer` (parameterless ctor, `string Sanitize(string html)`).
- Produces: same class/signature; allowlist additionally passes `table thead tbody tr th td sub sup span` tags and `style` attributes carrying only `color`/`text-align` CSS properties. `ItemService` picks this up automatically (already injected) — no other backend file changes.

- [ ] **Step 1: Rewrite the one existing test that pins the old "all inline style stripped" behaviour**

In `tests/Struo.Tests/Security/GanssHtmlSanitizerTests.cs`, replace the existing `Strips_iframe_and_inline_style` test (it asserts `NotContain("style=")` for `<p style="color:red">`, which 7g deliberately changes) with:

```csharp
    [Fact]
    public void Strips_iframe()
    {
        var clean = _s.Sanitize("<iframe src=\"http://evil\"></iframe><p>t</p>");
        clean.Should().NotContain("iframe");
        clean.Should().Contain("t");
    }
```

- [ ] **Step 2: Add the new failing tests**

Append to the same class:

```csharp
    [Fact]
    public void Keeps_basic_table_markup()
    {
        var html = "<table><tbody><tr><th><p>H</p></th></tr>"
                 + "<tr><td><p>c</p></td></tr></tbody></table>";
        var clean = _s.Sanitize(html);
        clean.Should().Contain("<table>").And.Contain("<tbody>").And.Contain("<tr>")
             .And.Contain("<th>").And.Contain("<td>");
    }

    [Fact]
    public void Strips_colspan_rowspan_and_colwidth_from_table_cells()
    {
        var clean = _s.Sanitize(
            "<table><tbody><tr><td colspan=\"2\" rowspan=\"3\" colwidth=\"120\"><p>c</p></td></tr></tbody></table>");
        clean.Should().NotContain("colspan").And.NotContain("rowspan").And.NotContain("colwidth");
        clean.Should().Contain("<td>");
    }

    [Fact]
    public void Keeps_text_align_style_on_paragraphs_and_headings()
    {
        var clean = _s.Sanitize(
            "<p style=\"text-align: center\">t</p><h2 style=\"text-align: right\">h</h2>");
        clean.Should().Contain("text-align").And.Contain("center").And.Contain("right");
    }

    [Fact]
    public void Keeps_color_style_on_span()
    {
        var clean = _s.Sanitize("<p><span style=\"color: #e11d48\">red</span></p>");
        clean.Should().Contain("<span").And.Contain("color");
        clean.Should().Contain("red");
    }

    [Fact]
    public void Strips_non_allowlisted_css_properties_but_keeps_allowed_ones()
    {
        var clean = _s.Sanitize(
            "<p style=\"text-align: center; position: fixed; font-size: 99px\">t</p>");
        clean.Should().Contain("text-align").And.NotContain("position").And.NotContain("font-size");
    }

    [Fact]
    public void Strips_dangerous_css_values()
    {
        var clean = _s.Sanitize(
            "<p style=\"background: url(//evil/x.png)\">a</p>"
          + "<p style=\"width: expression(alert(1))\">b</p>");
        clean.Should().NotContain("url(").And.NotContain("expression");
        clean.Should().Contain("a").And.Contain("b");
    }

    [Fact]
    public void Keeps_sub_and_sup()
    {
        var clean = _s.Sanitize("<p>H<sub>2</sub>O and x<sup>2</sup></p>");
        clean.Should().Contain("<sub>").And.Contain("<sup>");
    }
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run (repo root): `dotnet test --filter "FullyQualifiedName~GanssHtmlSanitizerTests"`
Expected: 6 of the 7 new tests FAIL (tags/styles stripped by the current allowlist). `Strips_dangerous_css_values` PASSES already — it is a pure regression guard pinning that the 7g relaxation must not let `url()`/`expression()` through. `Strips_iframe` and all other existing tests PASS.

- [ ] **Step 4: Extend the allowlist in `GanssHtmlSanitizer.cs`**

Replace the tag array, the attribute array, and the CSS-clearing block in the constructor:

```csharp
        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 { "p", "h2", "h3", "strong", "em", "s", "ul", "ol", "li",
                   "blockquote", "pre", "code", "hr", "br", "a", "img",
                   // 7g: basic tables + sub/superscript + the colour carrier tag.
                   "table", "thead", "tbody", "tr", "th", "td", "sub", "sup", "span" })
            _sanitizer.AllowedTags.Add(tag);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "src", "alt", "rel", "style" })
            _sanitizer.AllowedAttributes.Add(attr);
```

and

```csharp
        // 7g: style survives but carries exactly two CSS properties (colour + alignment);
        // Ganss strips every other property and dangerous values (url()/expression()) per-property.
        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedCssProperties.Add("color");
        _sanitizer.AllowedCssProperties.Add("text-align");
        _sanitizer.AllowedAtRules.Clear();
```

Also update the class XML-doc summary line to mention 7g: basic tables, text-align/colour styles, sub/superscript.

- [ ] **Step 5: Run the sanitizer tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~GanssHtmlSanitizerTests"`
Expected: ALL PASS (7 new + rewritten `Strips_iframe` + all pre-existing).

- [ ] **Step 6: Run the full backend suite (regression, incl. live-PG integration tests)**

Run: `dotnet test`
Expected: all green (316 baseline + 7 new − 0; the rewritten test replaces one). `ItemServiceRichTextSanitizationTests` must still pass — the write path is untouched.

- [ ] **Step 7: Commit**

```bash
git add tests/Struo.Tests/Security/GanssHtmlSanitizerTests.cs src/Struo.Infrastructure/Security/GanssHtmlSanitizer.cs
git commit -m "feat(sanitizer): allow basic tables, sub/sup, and color/text-align styles (7g)"
```

---

### Task 2: Frontend — TipTap extensions + align / sub / sup toolbar toggles

**Files:**
- Modify: `frontend/package.json` (via `pnpm add` only)
- Modify: `frontend/src/components/fields/RichTextInput.vue`
- Modify: `frontend/src/components/fields/RichTextInput.test.ts`

**Interfaces:**
- Consumes: existing `RichTextInput.vue` (props `{ modelValue: string; disabled?: boolean }`, emits `update:modelValue`; toolbar buttons carry `data-cmd` attributes; `defineExpose({ editor, insertImage })`).
- Produces: the editor additionally supports `table`, `textAlign`, `textStyle`+`color`, `subscript`, `superscript`; new toolbar buttons `data-cmd="alignLeft|alignCenter|alignRight|alignJustify|subscript|superscript"`. Tasks 3–4 mount their menus into this toolbar.

- [ ] **Step 1: Install the TipTap extension packages (versions from pnpm, never hand-authored)**

Run (in `frontend/`):

```bash
pnpm add @tiptap/extension-table @tiptap/extension-text-align @tiptap/extension-text-style @tiptap/extension-subscript @tiptap/extension-superscript
```

Expected: five deps added to `package.json` at pnpm-resolved versions (same 3.x line as the existing `@tiptap/*` deps).

- [ ] **Step 2: Write the failing tests**

Append to `RichTextInput.test.ts` (inside the existing `describe`, using the existing `stubs`):

```ts
  it('sets text alignment via the toolbar', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: { stubs } })
    await flushPromises()
    await w.get('[data-cmd="alignCenter"]').trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (a: Record<string, string>) => boolean } }
    expect(vm.editor.isActive({ textAlign: 'center' })).toBe(true)
  })

  it('subscript and superscript are mutually exclusive', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: { stubs } })
    await flushPromises()
    await w.get('[data-cmd="subscript"]').trigger('click')
    const vm = w.vm as unknown as { editor: { isActive: (n: string) => boolean } }
    expect(vm.editor.isActive('subscript')).toBe(true)
    await w.get('[data-cmd="superscript"]').trigger('click')
    expect(vm.editor.isActive('superscript')).toBe(true)
    expect(vm.editor.isActive('subscript')).toBe(false)
  })
```

- [ ] **Step 3: Run the new tests to verify they fail**

Run (in `frontend/`): `pnpm test -- RichTextInput`
Expected: the 2 new tests FAIL (`[data-cmd="alignCenter"]` not found); the 5 existing tests PASS.

- [ ] **Step 4: Wire the extensions and toolbar buttons**

In `RichTextInput.vue`, add imports:

```ts
import { TableKit } from '@tiptap/extension-table'
import TextAlign from '@tiptap/extension-text-align'
import { TextStyle, Color } from '@tiptap/extension-text-style'
import Subscript from '@tiptap/extension-subscript'
import Superscript from '@tiptap/extension-superscript'
```

(If the installed `@tiptap/extension-table` does not export `TableKit`, use the individual exports instead — `import { Table, TableRow, TableHeader, TableCell } from '@tiptap/extension-table'` — and register `Table.configure({ resizable: false }), TableRow, TableHeader, TableCell` below.)

Extend the `useEditor` extensions array (after `Image.configure(...)`):

```ts
    TableKit.configure({ table: { resizable: false } }),
    TextAlign.configure({ types: ['heading', 'paragraph'], alignments: ['left', 'center', 'right', 'justify'] }),
    TextStyle,
    Color,
    Subscript,
    Superscript,
```

Add toolbar buttons after the existing strike button (align group) and after the H-level buttons (sub/sup group), matching the existing button idiom exactly:

```html
      <button v-for="al in (['left', 'center', 'right', 'justify'] as const)" :key="al" type="button"
        :data-cmd="`align${al.charAt(0).toUpperCase()}${al.slice(1)}`"
        :class="{ active: editor.isActive({ textAlign: al }) }" :disabled="disabled"
        :aria-label="`Align ${al}`" :title="`Align ${al}`"
        @click="editor!.chain().focus().setTextAlign(al).run()"><i :class="`pi pi-align-${al}`" /></button>
      <button type="button" data-cmd="subscript" :class="{ active: editor.isActive('subscript') }"
        :disabled="disabled" aria-label="Subscript" title="Subscript"
        @click="editor!.chain().focus().toggleSubscript().run()">x₂</button>
      <button type="button" data-cmd="superscript" :class="{ active: editor.isActive('superscript') }"
        :disabled="disabled" aria-label="Superscript" title="Superscript"
        @click="editor!.chain().focus().toggleSuperscript().run()">x²</button>
```

Add table CSS to the scoped styles (ProseMirror tables render unstyled otherwise):

```css
.rich-text__content :deep(table) { border-collapse: collapse; width: 100%; margin: 8px 0; }
.rich-text__content :deep(th), .rich-text__content :deep(td) { border: 1px solid var(--surface-border, #d0d0d0); padding: 4px 8px; }
.rich-text__content :deep(th) { background: var(--surface-100, #f4f4f5); text-align: left; }
```

- [ ] **Step 5: Run the tests to verify they pass — and audit the console for duplicate-extension warnings**

Run: `pnpm test -- RichTextInput`
Expected: ALL PASS (7). Per the 7f StarterKit lesson: if the console shows a TipTap "duplicate extension" warning (e.g. StarterKit already bundling one of the new extensions), disable the StarterKit copy in `StarterKit.configure({...})` — the explicit configured instance must be the only one — and re-run.

- [ ] **Step 6: Commit**

```bash
git add package.json pnpm-lock.yaml src/components/fields/RichTextInput.vue src/components/fields/RichTextInput.test.ts
git commit -m "feat(frontend): rich-text tables/align/color/sub-sup extensions + align and sub/sup toggles (7g)"
```

---

### Task 3: Frontend — `RichTextColorMenu` (palette + free picker + clear)

**Files:**
- Create: `frontend/src/components/fields/RichTextColorMenu.vue`
- Create: `frontend/src/components/fields/RichTextColorMenu.test.ts`
- Modify: `frontend/src/components/fields/RichTextInput.vue` (mount + wire)
- Modify: `frontend/src/components/fields/RichTextInput.test.ts` (integration test)

**Interfaces:**
- Consumes: Task 2's editor (`TextStyle` + `Color` registered → `setColor(hex)` / `unsetColor()` commands, `editor.getAttributes('textStyle').color`).
- Produces: `RichTextColorMenu.vue` — props `{ disabled?: boolean; activeColor?: string | null }`, emits `pick(color: string)` and `clear()`. Purely presentational; the parent runs editor commands.

- [ ] **Step 1: Write the failing component tests**

`RichTextColorMenu.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import RichTextColorMenu from './RichTextColorMenu.vue'

describe('RichTextColorMenu', () => {
  it('opens the panel and emits pick for a palette swatch', async () => {
    const w = mount(RichTextColorMenu)
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-color="#dc2626"]').trigger('click')
    expect(w.emitted('pick')).toEqual([['#dc2626']])
    expect(w.find('.color-menu__panel').exists()).toBe(false) // closes after pick
  })

  it('emits pick from the free colour input', async () => {
    const w = mount(RichTextColorMenu)
    await w.get('[data-cmd="color"]').trigger('click')
    const input = w.get('[data-cmd="colorFree"]')
    await input.setValue('#123456')
    expect(w.emitted('pick')).toEqual([['#123456']])
  })

  it('emits clear', async () => {
    const w = mount(RichTextColorMenu)
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-cmd="colorClear"]').trigger('click')
    expect(w.emitted('clear')).toHaveLength(1)
  })

  it('disables the trigger when disabled', () => {
    const w = mount(RichTextColorMenu, { props: { disabled: true } })
    expect(w.get('[data-cmd="color"]').attributes('disabled')).toBeDefined()
  })
})
```

- [ ] **Step 2: Run to verify failure**

Run: `pnpm test -- RichTextColorMenu`
Expected: FAIL — cannot resolve `./RichTextColorMenu.vue`.

- [ ] **Step 3: Implement the component**

`RichTextColorMenu.vue`:

```vue
<script setup lang="ts">
import { ref } from 'vue'

defineOptions({ name: 'RichTextColorMenu' })

defineProps<{ disabled?: boolean; activeColor?: string | null }>()
const emit = defineEmits<{ (e: 'pick', color: string): void; (e: 'clear'): void }>()

const open = ref(false)

const PALETTE = [
  '#0f172a', '#64748b', '#dc2626', '#ea580c', '#ca8a04',
  '#16a34a', '#0891b2', '#2563eb', '#7c3aed', '#db2777',
] as const

function pick(color: string): void {
  emit('pick', color)
  open.value = false
}

function onFreePick(e: Event): void {
  emit('pick', (e.target as HTMLInputElement).value)
}

function clear(): void {
  emit('clear')
  open.value = false
}
</script>

<template>
  <div class="color-menu">
    <button type="button" data-cmd="color" :disabled="disabled" aria-label="Text colour" title="Text colour"
      class="color-menu__trigger" @click="open = !open">
      <span :style="activeColor ? { color: activeColor } : undefined">A</span>
    </button>
    <div v-if="open" class="color-menu__panel">
      <div class="color-menu__swatches">
        <button v-for="c in PALETTE" :key="c" type="button" class="color-menu__swatch" :data-color="c"
          :style="{ background: c }" :aria-label="`Colour ${c}`" @click="pick(c)" />
      </div>
      <label class="color-menu__free">
        <input type="color" data-cmd="colorFree" @change="onFreePick" />
        Custom…
      </label>
      <button type="button" data-cmd="colorClear" class="color-menu__clear" @click="clear">Clear colour</button>
    </div>
  </div>
</template>

<style scoped>
.color-menu { position: relative; display: inline-block; }
.color-menu__trigger { min-width: 30px; padding: 2px 6px; cursor: pointer; background: transparent; border: 1px solid transparent; border-radius: 4px; font-weight: 700; }
.color-menu__trigger:disabled { opacity: 0.5; cursor: not-allowed; }
.color-menu__panel { position: absolute; z-index: 10; top: 100%; left: 0; margin-top: 4px; padding: 8px; background: var(--surface-0, #fff); border: 1px solid var(--surface-border, #d0d0d0); border-radius: 6px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15); width: max-content; }
.color-menu__swatches { display: grid; grid-template-columns: repeat(5, 22px); gap: 6px; }
.color-menu__swatch { width: 22px; height: 22px; border: 1px solid var(--surface-border, #d0d0d0); border-radius: 4px; cursor: pointer; }
.color-menu__free { display: flex; align-items: center; gap: 6px; margin-top: 8px; font-size: 0.85rem; cursor: pointer; }
.color-menu__clear { display: block; margin-top: 8px; width: 100%; padding: 2px 6px; cursor: pointer; background: transparent; border: 1px solid var(--surface-border, #d0d0d0); border-radius: 4px; }
</style>
```

- [ ] **Step 4: Run the component tests to verify they pass**

Run: `pnpm test -- RichTextColorMenu`
Expected: 4/4 PASS.

- [ ] **Step 5: Write the failing integration test (parent wiring)**

Append to `RichTextInput.test.ts`:

```ts
  it('applies colour via the colour menu', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>abc</p>' }, global: { stubs } })
    await flushPromises()
    const vm = w.vm as unknown as {
      editor: { commands: { selectAll: () => void }; getAttributes: (n: string) => Record<string, unknown> }
    }
    vm.editor.commands.selectAll()
    await w.get('[data-cmd="color"]').trigger('click')
    await w.get('[data-color="#dc2626"]').trigger('click')
    expect(vm.editor.getAttributes('textStyle').color).toBe('#dc2626')
  })
```

Run: `pnpm test -- RichTextInput` — Expected: the new test FAILS (`[data-cmd="color"]` not found).

- [ ] **Step 6: Mount and wire in `RichTextInput.vue`**

Import and mount after the image button in the toolbar:

```ts
import RichTextColorMenu from './RichTextColorMenu.vue'
```

```html
      <RichTextColorMenu :disabled="disabled"
        :active-color="(editor.getAttributes('textStyle').color as string | undefined) ?? null"
        @pick="(c: string) => editor!.chain().focus().setColor(c).run()"
        @clear="editor!.chain().focus().unsetColor().run()" />
```

- [ ] **Step 7: Run all RichText tests to verify they pass**

Run: `pnpm test -- RichText`
Expected: ALL PASS (RichTextInput 8 + RichTextColorMenu 4).

- [ ] **Step 8: Commit**

```bash
git add src/components/fields/RichTextColorMenu.vue src/components/fields/RichTextColorMenu.test.ts src/components/fields/RichTextInput.vue src/components/fields/RichTextInput.test.ts
git commit -m "feat(frontend): rich-text colour menu (palette + free picker + clear) (7g)"
```

---

### Task 4: Frontend — `RichTextTableMenu` (insert / row-col ops / header toggle / delete)

**Files:**
- Create: `frontend/src/components/fields/richTextTableActions.ts`
- Create: `frontend/src/components/fields/RichTextTableMenu.vue`
- Create: `frontend/src/components/fields/RichTextTableMenu.test.ts`
- Modify: `frontend/src/components/fields/RichTextInput.vue` (mount + wire)
- Modify: `frontend/src/components/fields/RichTextInput.test.ts` (integration test)

**Interfaces:**
- Consumes: Task 2's editor (`TableKit` registered → `insertTable/addRowBefore/addRowAfter/addColumnBefore/addColumnAfter/deleteRow/deleteColumn/toggleHeaderRow/deleteTable` commands, `editor.isActive('table')`).
- Produces: `richTextTableActions.ts` — `export type TableAction` (union of the 9 action names) + `export const IN_TABLE_ACTIONS: ReadonlyArray<readonly [TableAction, string]>` (the 8 in-table actions with labels). `RichTextTableMenu.vue` — props `{ disabled?: boolean; inTable: boolean }`, emits `action(action: TableAction)`. Purely presentational.

- [ ] **Step 1: Create the shared action-type module**

`richTextTableActions.ts`:

```ts
export type TableAction =
  | 'insert'
  | 'addRowBefore'
  | 'addRowAfter'
  | 'addColumnBefore'
  | 'addColumnAfter'
  | 'deleteRow'
  | 'deleteColumn'
  | 'toggleHeaderRow'
  | 'deleteTable'

export const IN_TABLE_ACTIONS: ReadonlyArray<readonly [TableAction, string]> = [
  ['addRowBefore', 'Add row above'],
  ['addRowAfter', 'Add row below'],
  ['addColumnBefore', 'Add column left'],
  ['addColumnAfter', 'Add column right'],
  ['deleteRow', 'Delete row'],
  ['deleteColumn', 'Delete column'],
  ['toggleHeaderRow', 'Toggle header row'],
  ['deleteTable', 'Delete table'],
]
```

- [ ] **Step 2: Write the failing component tests**

`RichTextTableMenu.test.ts`:

```ts
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import RichTextTableMenu from './RichTextTableMenu.vue'

describe('RichTextTableMenu', () => {
  it('emits insert and closes', async () => {
    const w = mount(RichTextTableMenu, { props: { inTable: false } })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableInsert"]').trigger('click')
    expect(w.emitted('action')).toEqual([['insert']])
    expect(w.find('.table-menu__panel').exists()).toBe(false)
  })

  it('disables in-table actions when outside a table', async () => {
    const w = mount(RichTextTableMenu, { props: { inTable: false } })
    await w.get('[data-cmd="table"]').trigger('click')
    expect(w.get('[data-cmd="table-deleteTable"]').attributes('disabled')).toBeDefined()
    expect(w.get('[data-cmd="tableInsert"]').attributes('disabled')).toBeUndefined()
  })

  it('emits in-table actions when inside a table', async () => {
    const w = mount(RichTextTableMenu, { props: { inTable: true } })
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="table-addRowAfter"]').trigger('click')
    expect(w.emitted('action')).toEqual([['addRowAfter']])
  })

  it('disables the trigger when disabled', () => {
    const w = mount(RichTextTableMenu, { props: { inTable: false, disabled: true } })
    expect(w.get('[data-cmd="table"]').attributes('disabled')).toBeDefined()
  })
})
```

Run: `pnpm test -- RichTextTableMenu` — Expected: FAIL (component missing).

- [ ] **Step 3: Implement the component**

`RichTextTableMenu.vue`:

```vue
<script setup lang="ts">
import { ref } from 'vue'
import { IN_TABLE_ACTIONS, type TableAction } from './richTextTableActions'

defineOptions({ name: 'RichTextTableMenu' })

defineProps<{ disabled?: boolean; inTable: boolean }>()
const emit = defineEmits<{ (e: 'action', action: TableAction): void }>()

const open = ref(false)
const inTableActions = IN_TABLE_ACTIONS

function run(action: TableAction): void {
  emit('action', action)
  open.value = false
}
</script>

<template>
  <div class="table-menu">
    <button type="button" data-cmd="table" :disabled="disabled" aria-label="Table" title="Table"
      class="table-menu__trigger" @click="open = !open"><i class="pi pi-table" /></button>
    <div v-if="open" class="table-menu__panel">
      <button type="button" data-cmd="tableInsert" class="table-menu__item" @click="run('insert')">
        Insert 3×3 table
      </button>
      <button v-for="[action, label] in inTableActions" :key="action" type="button"
        :data-cmd="`table-${action}`" class="table-menu__item" :disabled="!inTable"
        @click="run(action)">{{ label }}</button>
    </div>
  </div>
</template>

<style scoped>
.table-menu { position: relative; display: inline-block; }
.table-menu__trigger { min-width: 30px; padding: 2px 6px; cursor: pointer; background: transparent; border: 1px solid transparent; border-radius: 4px; }
.table-menu__trigger:disabled { opacity: 0.5; cursor: not-allowed; }
.table-menu__panel { position: absolute; z-index: 10; top: 100%; left: 0; margin-top: 4px; padding: 4px; background: var(--surface-0, #fff); border: 1px solid var(--surface-border, #d0d0d0); border-radius: 6px; box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15); width: max-content; display: flex; flex-direction: column; }
.table-menu__item { text-align: left; padding: 4px 10px; cursor: pointer; background: transparent; border: none; border-radius: 4px; }
.table-menu__item:hover:not(:disabled) { background: var(--surface-100, #f4f4f5); }
.table-menu__item:disabled { opacity: 0.5; cursor: not-allowed; }
</style>
```

- [ ] **Step 4: Run the component tests to verify they pass**

Run: `pnpm test -- RichTextTableMenu`
Expected: 4/4 PASS.

- [ ] **Step 5: Write the failing integration test (parent wiring)**

Append to `RichTextInput.test.ts`:

```ts
  it('inserts a 3x3 table with header row via the table menu', async () => {
    const w = mount(RichTextInput, { props: { modelValue: '<p>a</p>' }, global: { stubs } })
    await flushPromises()
    await w.get('[data-cmd="table"]').trigger('click')
    await w.get('[data-cmd="tableInsert"]').trigger('click')
    await flushPromises()
    const emitted = w.emitted('update:modelValue')
    const html = String(emitted!.at(-1)![0])
    expect(html).toContain('<table')
    expect(html).toContain('<th')
  })
```

Run: `pnpm test -- RichTextInput` — Expected: the new test FAILS (`[data-cmd="table"]` not found).

- [ ] **Step 6: Mount and wire in `RichTextInput.vue`**

Imports:

```ts
import RichTextTableMenu from './RichTextTableMenu.vue'
import type { TableAction } from './richTextTableActions'
```

Handler (in the script, after `setLink`):

```ts
function onTableAction(action: TableAction): void {
  if (!editor.value) return
  const chain = editor.value.chain().focus()
  const commands: Record<TableAction, () => void> = {
    insert: () => chain.insertTable({ rows: 3, cols: 3, withHeaderRow: true }).run(),
    addRowBefore: () => chain.addRowBefore().run(),
    addRowAfter: () => chain.addRowAfter().run(),
    addColumnBefore: () => chain.addColumnBefore().run(),
    addColumnAfter: () => chain.addColumnAfter().run(),
    deleteRow: () => chain.deleteRow().run(),
    deleteColumn: () => chain.deleteColumn().run(),
    toggleHeaderRow: () => chain.toggleHeaderRow().run(),
    deleteTable: () => chain.deleteTable().run(),
  }
  commands[action]()
}
```

Template (next to the colour menu in the toolbar):

```html
      <RichTextTableMenu :disabled="disabled" :in-table="editor.isActive('table')" @action="onTableAction" />
```

- [ ] **Step 7: Run the full frontend suite to verify green**

Run: `pnpm test`
Expected: ALL PASS (161 baseline + 2 Task-2 + 5 Task-3 + 5 Task-4 = 173).

- [ ] **Step 8: Commit**

```bash
git add src/components/fields/richTextTableActions.ts src/components/fields/RichTextTableMenu.vue src/components/fields/RichTextTableMenu.test.ts src/components/fields/RichTextInput.vue src/components/fields/RichTextInput.test.ts
git commit -m "feat(frontend): rich-text table menu (insert/rows/cols/header/delete) (7g)"
```

---

### Task 5: Full gates + docs + merge readiness

**Files:**
- Modify: `docs/ROADMAP.md` (7g row + verification baseline)

**Interfaces:**
- Consumes: everything above.
- Produces: a merge-ready branch; live-gate steps for the operator-driven verification.

- [ ] **Step 1: Run every automated gate**

```bash
# repo root
dotnet build          # expected: clean, warnings-as-errors
dotnet test           # expected: all green (Task 1 count)
# frontend/
pnpm test             # expected: all green (Task 4 count)
pnpm build            # expected: succeeds (pre-existing >500 kB chunk advisory only)
```

- [ ] **Step 2: Update `docs/ROADMAP.md`**

Split the current `7g+` row into a `7g` row — "Advanced rich text (basic tables / text-align / colour / sub-superscript), sanitizer allowlist extended in lockstep (`style` limited to `color`+`text-align`)" with status "code-complete, live-gate pending", linking spec `superpowers/specs/2026-07-06-phase7g-advanced-richtext-design.md` and plan `superpowers/plans/2026-07-06-phase7g-advanced-richtext.md` — and a remaining `7g+` row (multi-value selects, structured editors, multi-file `Files`). Update the "Next up" line and add a post-7g verification-baseline entry with the actual test counts from Step 1.

- [ ] **Step 3: Commit**

```bash
git add docs/ROADMAP.md
git commit -m "docs: Phase 7g code-complete (advanced rich text); automated gates green, live-gate pending"
```

- [ ] **Step 4: Live gate (operator-driven, real Postgres + Redis + MinIO — per project convention)**

API-level via PowerShell + curl.exe with UTF-8 no-BOM payload files and a cookie jar (NOT Big5 Git Bash — see memory `live-verify-utf8-encoding`):

1. **Round-trip preservation:** create an `article` whose `body` contains a 2×2 table with header row + `<p style="text-align: center">` + `<h2 style="text-align: right">` + `<span style="color: #dc2626">` + `H<sub>2</sub>O` + `x<sup>2</sup>` → GET reads back with table structure, both alignments, the colour span, and sub/sup intact.
2. **Hostile payload:** a `body` with `style="position:fixed"`, `style="background:url(//evil)"`, `style="width:expression(alert(1))"`, `colspan="5"`, plus the 7f vector suite (`<script>`, `onclick`, `javascript:` href, `<iframe>`, `data:` img src) → GET confirms only the allowed subset survives.
3. **i18n:** `en` + `zh-TW` bodies containing the new formatting round-trip with correct UTF-8.

Record PASS/FAIL evidence, then update the ROADMAP row to "done & live-verified" before merging (merge handled via superpowers:finishing-a-development-branch).
