import { readdirSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitepress'
import { firstLink } from '../scripts/lib/sidebar-links.mjs'
import { chapterSidebar } from './sidebar.mts'

const GUIDE_ROOT = join(dirname(fileURLToPath(import.meta.url)), '..', 'guide')

// Mirrors the guard's own locale derivation
// (docs/scripts/check-rendered-chapters.mjs): a locale is a directory under
// guide/, dot-prefixed ones excluded (tooling on a developer's machine can
// drop dot-prefixed working directories under guide/, and a dot-prefixed
// directory is never a locale).
// A fork that deletes docs/guide/zh-TW/ then loses that locale here instead of
// crashing config load with a bare `ENOENT: … scandir …/guide/zh-TW`.
const AVAILABLE_LOCALES = new Set(
  readdirSync(GUIDE_ROOT, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && !entry.name.startsWith('.'))
    .map((entry) => entry.name),
)

// Metadata VitePress needs per locale that cannot be derived from a directory
// name — label, lang and the changelog link's text are prose choices, not
// filesystem facts. Only entries whose directory actually exists
// (AVAILABLE_LOCALES) make it into `locales` below, so removing a locale
// directory removes the locale, not the config.
const LOCALE_METADATA: Record<string, { label: string; lang: string; changelogLabel: string }> = {
  en: { label: 'English', lang: 'en', changelogLabel: 'Changelog' },
  'zh-TW': { label: '繁體中文', lang: 'zh-TW', changelogLabel: '版本紀錄' },
}

// The link is the sidebar's own first entry, not a hand-copied filename: a
// renamed chapter 1 would otherwise break the locale switcher without
// ignoreDeadLinks ever seeing it, since this is config, not content. Reading
// it via firstLink rather than sidebar[0].link keeps this correct whatever
// shape chapterSidebar returns — a flat link or a group — so the two files
// can change independently.
function localeConfig(key: string) {
  const sidebar = chapterSidebar(key)
  const { label, lang, changelogLabel } = LOCALE_METADATA[key]
  return {
    label,
    lang,
    link: firstLink(sidebar),
    // The changelog is guide/<locale>/changelog.md — not an NN- chapter, so the sidebar deriver
    // skips it; the nav is its only entry point.
    themeConfig: { sidebar, nav: [{ text: changelogLabel, link: `/${key}/changelog` }] },
  }
}

export default defineConfig({
  title: 'StruoCMS Manual',
  description: 'The StruoCMS template manual, in English and Traditional Chinese.',

  // Content lives in docs/guide/{en,zh-TW}/, which is exactly VitePress's
  // locale-prefix convention one level down. Pointing srcDir at guide/ makes
  // the two directories the two locales with no file moved. It also scopes
  // content to guide/ by construction: docs/ai/, docs/README.md and
  // docs/_archive-local/ sit outside srcDir, so they are never scanned,
  // rendered, or indexed. .vitepress/ itself stays at the project root.
  srcDir: 'guide',

  // The default, stated because it is load-bearing: the manual's cross-chapter
  // links are bare sibling filenames, and this setting is what checks them —
  // a renamed or deleted chapter fails the build with a dead-link error.
  // It does not catch every broken page: an unprotected `{{ }}` in rendered
  // text is a separate failure mode that this setting has no bearing on;
  // see docs/ai/conventions.md's "Mustache syntax in the manual" section and
  // docs/scripts/check-rendered-chapters.mjs, which guards against it.
  ignoreDeadLinks: false,

  locales: Object.fromEntries(
    Object.keys(LOCALE_METADATA)
      .filter((key) => AVAILABLE_LOCALES.has(key))
      .map((key) => [key, localeConfig(key)]),
  ),

  themeConfig: {
    search: {
      provider: 'local',
      options: {
        locales: {
          'zh-TW': {
            translations: {
              button: { buttonText: '搜尋', buttonAriaLabel: '搜尋' },
              modal: {
                displayDetails: '顯示詳細內容',
                resetButtonTitle: '清除',
                backButtonTitle: '關閉搜尋',
                noResultsText: '找不到結果',
                footer: {
                  selectText: '選擇',
                  navigateText: '切換',
                  closeText: '關閉',
                },
              },
            },
          },
        },
      },
    },
  },
})
