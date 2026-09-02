/// <reference types="vitest/config" />
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import tailwindcss from '@tailwindcss/vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue(), tailwindcss()],
  // Several route/async entry points (the RichText field, its slash-menu views) each pull in
  // TipTap + ProseMirror. Left alone, every async chunk that imports it would bundle its own
  // copy; grouping the whole family into one named vendor chunk means the browser downloads
  // TipTap once and every entry point that needs it shares that same chunk.
  build: {
    rolldownOptions: {
      output: {
        codeSplitting: {
          groups: [
            { name: 'tiptap', test: /node_modules[\\/](@tiptap|prosemirror-)/ },
          ],
        },
      },
    },
  },
  // Shared by `vite build`, `vite dev` and Vitest — Vitest reads this same config,
  // so one alias entry covers the app, the type-check and the test run.
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: {
    port: 5173,
    // Pinned to the IPv4 loopback: on some Windows setups "localhost" resolves to the IPv6
    // loopback first, Vite then binds only [::1], and a Chromium client (real browser or
    // Playwright) navigating to http://localhost:5173 cannot connect. Binding 127.0.0.1
    // explicitly is unambiguous and works everywhere loopback access is needed.
    host: '127.0.0.1',
    // Same-origin dev/E2E: browser hits /api on the Vite origin, proxied to the API.
    proxy: { '/api': { target: 'http://localhost:5221', changeOrigin: true } },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    // Every spy is restored and every mock's call history cleared between tests. Without these,
    // a spy installed by one test stays installed for the rest of the file, and this repository
    // has already shipped a bug of exactly that shape. tests/testIsolation.test.ts
    // pins both. These fire before each test, so a spy installed in `beforeAll` is already
    // restored by the time the first test runs; install spies in `beforeEach` or in the test.
    restoreMocks: true,
    clearMocks: true,
    setupFiles: ['./vitest.setup.ts'],
    include: ['src/**/*.{test,spec}.ts', 'tests/**/*.{test,spec}.ts'],
    exclude: ['**/node_modules/**', '**/dist/**', '**/e2e/**'],
    coverage: { provider: 'v8', reportsDirectory: './coverage' },
  },
})
