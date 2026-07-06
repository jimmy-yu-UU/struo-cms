// Global test setup (PrimeVue config, etc.).

// jsdom does not implement matchMedia; PrimeVue's DatePicker uses it to bind a
// responsive-breakpoint listener on mount. Stub a minimal no-op implementation
// so components mounting a real DatePicker don't crash under jsdom.
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

export {}
