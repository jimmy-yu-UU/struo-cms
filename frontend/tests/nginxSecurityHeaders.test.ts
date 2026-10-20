import { describe, it, expect } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

const root = resolve(__dirname, '..')
const rawTemplate = readFileSync(resolve(root, 'nginx/default.conf.template'), 'utf8')
// Placeholders are masked so their braces do not end a location body.
const template = rawTemplate.replace(/^\s*#.*$/gm, '').replace(/\$\{(\w+)\}/g, '<<$1>>')
const unmask = (text: string) => text.replace(/<<(\w+)>>/g, '${$1}')
const indexHtml = readFileSync(resolve(root, 'index.html'), 'utf8')

const APP_CSP =
  "Content-Security-Policy \"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
  'img-src \'self\' data: blob: ${CSP_IMG_EXTRA}; connect-src \'self\' ${CSP_CONNECT_EXTRA}; ' +
  "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'\""
const API_CSP = "Content-Security-Policy \"default-src 'none'; frame-ancestors 'none'; sandbox\""
const PERMISSIONS =
  'Permissions-Policy "camera=(), microphone=(), geolocation=(), payment=(), usb=()"'

interface Block { name: string; body: string }

// Splits the server block into its own directives and each `location` body.
function blocks(): Block[] {
  const result: Block[] = []
  const locationRe = /^\s*location\s+([^{]+)\{([^}]*)\}/gm
  let m: RegExpExecArray | null
  let rest = template
  while ((m = locationRe.exec(template)) !== null) {
    result.push({ name: m[1].trim(), body: unmask(m[2]) })
    rest = rest.replace(m[0], '')
  }
  result.push({ name: 'server', body: unmask(rest) })
  return result
}

const withHeaders = blocks().filter((b) => /add_header/.test(b.body))

describe('nginx template security headers', () => {
  it('finds the server block and the add_header locations', () => {
    const names = withHeaders.map((b) => b.name)
    expect(names).toEqual(expect.arrayContaining(['server', '= /index.html', '/assets/']))
  })

  it.each(withHeaders.map((b) => [b.name, b] as const))(
    'block %s declares Permissions-Policy and a Content-Security-Policy',
    (_name, b) => {
      expect(b.body).toContain(PERMISSIONS)
      expect(b.body).toMatch(/add_header Content-Security-Policy /)
    },
  )

  it.each(withHeaders.filter((b) => b.name !== '/api/').map((b) => [b.name, b] as const))(
    'block %s carries the application policy',
    (_name, b) => {
      expect(b.body).toContain(`add_header ${APP_CSP} always;`)
    },
  )

  it('the /api/ location carries the sandbox policy and hides the upstream CSP', () => {
    const api = blocks().find((b) => b.name === '/api/')!
    expect(api.body).toContain(`add_header ${API_CSP} always;`)
    expect(api.body).toContain('proxy_hide_header Content-Security-Policy;')
  })

  it('the /api/ location repeats the other security headers', () => {
    const api = blocks().find((b) => b.name === '/api/')!
    for (const h of ['X-Content-Type-Options', 'X-Frame-Options', 'Referrer-Policy', 'Strict-Transport-Security']) {
      expect(api.body).toContain(`add_header ${h} `)
    }
  })
})

describe('index.html', () => {
  it('has no inline script body', () => {
    const scripts = [...indexHtml.matchAll(/<script\b([^>]*)>([\s\S]*?)<\/script>/g)]
    expect(scripts.length).toBeGreaterThan(0)
    for (const s of scripts) expect(s[2].trim()).toBe('')
  })

  it('loads theme-init.js before the app module', () => {
    const theme = indexHtml.indexOf('src="/theme-init.js"')
    const app = indexHtml.indexOf('src="/src/main.ts"')
    expect(theme).toBeGreaterThan(-1)
    expect(theme).toBeLessThan(app)
  })
})
