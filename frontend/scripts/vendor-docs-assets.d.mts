// Type declarations for vendor-docs-assets.mjs, so frontend/tests/docsVendorAssets.test.ts (and any
// other TypeScript consumer) gets real types instead of implicit `any` -- vue-tsc has no other way
// to type-check a plain .mjs module.

/** Absolute path to frontend/node_modules, resolved from this script's own location. */
export declare const NODE_MODULES: string

/** Absolute path to docs/vendor/, resolved from this script's own location. */
export declare const VENDOR_ROOT: string

/**
 * [source path under node_modules, destination path under docs/vendor/] pairs. The single source
 * of truth for what gets vendored -- both the copy script and the drift-guard test read this.
 */
export declare const FILES: readonly (readonly [string, string])[]
