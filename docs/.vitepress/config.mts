import { defineConfig } from 'vitepress'
import { chapterSidebar } from './sidebar.mts'

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
  // text is a separate failure mode (see the two v-pre wraps in chapter 7)
  // that this setting has no bearing on.
  ignoreDeadLinks: false,

  locales: {
    en: {
      label: 'English',
      lang: 'en',
      link: '/en/01-introduction-and-architecture',
      themeConfig: {
        sidebar: chapterSidebar('en'),
      },
    },
    'zh-TW': {
      label: '繁體中文',
      lang: 'zh-TW',
      link: '/zh-TW/01-introduction-and-architecture',
      themeConfig: {
        sidebar: chapterSidebar('zh-TW'),
      },
    },
  },

  themeConfig: {
    search: { provider: 'local' },
  },
})
