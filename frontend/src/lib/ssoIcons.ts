// Icons for the login page's SSO buttons, looked up by the `icon` a provider reports in /api/config
// (`Oidc:Providers:<id>:Icon`). To add a brand mark in a fork: add an entry to ICONS below, keyed by
// a lowercase name, and set that name as the provider's Icon. Marks are inline SVG, never remote
// URLs (the CSP allows no remote images here). An empty or unknown name shows the generic key icon.
import { defineComponent, h, type Component } from 'vue'
import { KeyRound } from '@lucide/vue'

function svgIcon(name: string, viewBox: string, children: () => ReturnType<typeof h>[]): Component {
  return defineComponent({
    name,
    inheritAttrs: true,
    setup: () => () => h('svg', { viewBox, 'aria-hidden': 'true' }, children()),
  })
}

const Microsoft = svgIcon('SsoMicrosoftIcon', '0 0 21 21', () => [
  h('rect', { width: 10, height: 10, fill: '#f25022' }),
  h('rect', { x: 11, width: 10, height: 10, fill: '#7fba00' }),
  h('rect', { y: 11, width: 10, height: 10, fill: '#00a4ef' }),
  h('rect', { x: 11, y: 11, width: 10, height: 10, fill: '#ffb900' }),
])

const Google = svgIcon('SsoGoogleIcon', '0 0 48 48', () => [
  h('path', {
    fill: '#EA4335',
    d: 'M24 9.5c3.54 0 6.71 1.22 9.21 3.6l6.85-6.85C35.9 2.38 30.47 0 24 0 14.62 0 6.51 5.38 2.56 13.22l7.98 6.19C12.43 13.72 17.74 9.5 24 9.5z',
  }),
  h('path', {
    fill: '#4285F4',
    d: 'M46.98 24.55c0-1.57-.15-3.09-.38-4.55H24v9.02h12.94c-.58 2.96-2.26 5.48-4.78 7.18l7.73 6c4.51-4.18 7.09-10.36 7.09-17.65z',
  }),
  h('path', {
    fill: '#FBBC05',
    d: 'M10.53 28.59c-.48-1.45-.76-2.99-.76-4.59s.27-3.14.76-4.59l-7.98-6.19C.92 16.46 0 20.12 0 24c0 3.88.92 7.54 2.56 10.78l7.97-6.19z',
  }),
  h('path', {
    fill: '#34A853',
    d: 'M24 48c6.48 0 11.93-2.13 15.89-5.81l-7.73-6c-2.15 1.45-4.92 2.3-8.16 2.3-6.26 0-11.57-4.22-13.47-9.91l-7.98 6.19C6.51 42.62 14.62 48 24 48z',
  }),
])

const ICONS: Record<string, Component> = {
  microsoft: Microsoft,
  google: Google,
}

export function resolveSsoIcon(name: string | null | undefined): Component {
  const key = (name ?? '').trim().toLowerCase()
  return Object.hasOwn(ICONS, key) ? ICONS[key] : KeyRound
}
