// Global test setup: jsdom gaps that Vue component libraries assume are present.
//
// reka-ui's floating components (Select, Combobox, DropdownMenu, Popover, Tooltip) and
// the shadcn SidebarProvider all touch browser APIs jsdom does not implement. Stub them
// once here rather than per test file — a missing stub surfaces as an unrelated crash
// deep inside a component, which is expensive to diagnose.
//
// Two reka-ui-wide test facts that CAN'T be hoisted into this file's own config (they're
// per-mount()/per-interaction, not jsdom polyfills) live here anyway, as the one place their WHY
// is written down — every affected test file just repeats the mechanics and points back here:
//   1. reka-ui's own portal wrapper component is itself named "Teleport" (it renders the real
//      <Teleport> only after mount). vue-test-utils' default `teleport` stub only special-cases
//      Vue's own built-in Teleport, so any reka-ui floating component (Select, DropdownMenu,
//      AlertDialog, ...) needs `stubs: { teleport: true }` matched by name, PLUS
//      `renderStubDefaultSlot: true` so the portaled content stays inside the mounted tree
//      instead of vanishing.
//   2. reka-ui's SelectTrigger opens on pointerdown (not click) and SelectItem selects on
//      pointerup (not click) — see reka-ui/dist/Select/{SelectTrigger,SelectItem}.js. A plain
//      `.trigger('click')` never reaches either handler; drive the real pointer events instead.

if (typeof window !== 'undefined' && !window.matchMedia) {
  window.matchMedia = (query: string): MediaQueryList => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: () => {},
    removeListener: () => {},
    addEventListener: () => {},
    removeEventListener: () => {},
    dispatchEvent: () => false,
  }) as MediaQueryList
}

class StubObserver {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
  takeRecords(): [] { return [] }
}

if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = StubObserver as unknown as typeof ResizeObserver
}
if (typeof globalThis.IntersectionObserver === 'undefined') {
  globalThis.IntersectionObserver = StubObserver as unknown as typeof IntersectionObserver
}

if (typeof Element !== 'undefined') {
  // Pointer capture drives reka-ui's press-and-drag interactions (Select, Slider).
  Element.prototype.hasPointerCapture ??= () => false
  Element.prototype.setPointerCapture ??= () => {}
  Element.prototype.releasePointerCapture ??= () => {}
  // Listbox implementations scroll the active option into view on open / arrow-key move.
  Element.prototype.scrollIntoView ??= () => {}
}

export {}
