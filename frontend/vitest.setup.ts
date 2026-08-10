// Global test setup: jsdom gaps that Vue component libraries assume are present.
//
// reka-ui's floating components (Select, Combobox, DropdownMenu, Popover, Tooltip) and
// the shadcn SidebarProvider all touch browser APIs jsdom does not implement. Stub them
// once here rather than per test file — a missing stub surfaces as an unrelated crash
// deep inside a component, which is expensive to diagnose.

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
