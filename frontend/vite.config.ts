/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
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
    setupFiles: ['./vitest.setup.ts'],
    include: ['src/**/*.{test,spec}.ts', 'tests/**/*.{test,spec}.ts'],
    exclude: ['**/node_modules/**', '**/dist/**', '**/e2e/**'],
    coverage: { provider: 'v8', reportsDirectory: './coverage' },
  },
})
