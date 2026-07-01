/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

// https://vite.dev/config/
export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    // Same-origin dev/E2E: browser hits /api on the Vite origin, proxied to the API.
    proxy: { '/api': { target: 'http://localhost:5080', changeOrigin: true } },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: ['./vitest.setup.ts'],
    coverage: { provider: 'v8', reportsDirectory: './coverage' },
  },
})
