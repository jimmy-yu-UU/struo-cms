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
const SECURITY_HEADERS = [
  'X-Content-Type-Options', 'X-Frame-Options', 'Referrer-Policy',
  'Strict-Transport-Security', 'Content-Security-Policy', 'Permissions-Policy',
]
const PERMISSIONS =
  'Permissions-Policy "camera=(), microphone=(), geolocation=(), payment=(), usb=()"'

interface Block { name: string; body: string }

// Splits a masked template into the server's own directives and one block per `location`.
// Braces are counted rather than matched with a pattern, so an `if { }` or a regex quantifier
// inside a location cannot cut its body short; a location nested in another throws.
function parseBlocks(text: string): Block[] {
  const result: Block[] = []
  let rest = text
  const headRe = /^[ \t]*location\s+(.+?)\s*\{[ \t]*$/m
  let m: RegExpExecArray | null
  while ((m = headRe.exec(rest)) !== null) {
    const start = m.index + m[0].length
    let depth = 1
    let i = start
    for (; i < rest.length && depth > 0; i++) {
      if (rest[i] === '{') depth++
      else if (rest[i] === '}') depth--
    }
    if (depth !== 0) throw new Error(`unterminated location ${m[1]}`)
    const body = rest.slice(start, i - 1)
    if (/^[ \t]*location\s/m.test(body)) throw new Error(`nested location inside ${m[1]}`)
    result.push({ name: m[1].trim(), body: unmask(body) })
    rest = rest.slice(0, m.index) + rest.slice(i)
  }
  result.push({ name: 'server', body: unmask(rest) })
  return result
}

function blocks(): Block[] {
  return parseBlocks(template)
}

const withHeaders = blocks().filter((b) => /add_header/.test(b.body))
// Locations that forward to the API: /api/ and the two OIDC callback paths.
const PROXIED = ['/api/', '= /signin-oidc', '= /signout-callback-oidc']

describe('nginx template parser', () => {
  it('keeps an if block inside its location body', () => {
    const parsed = parseBlocks(`location /x {
  if ($a) {
    return 403;
  }
  add_header X-A "1" always;
}
server_tokens off;`)
    expect(parsed.find((b) => b.name === '/x')!.body).toContain('add_header X-A')
    expect(parsed.find((b) => b.name === 'server')!.body).not.toContain('add_header')
  })

  it('keeps a regex quantifier inside the location head', () => {
    const parsed = parseBlocks(`location ~ ^/f{2}$ {
  add_header X-A "1" always;
}`)
    expect(parsed.find((b) => b.name === '~ ^/f{2}$')!.body).toContain('add_header X-A')
  })

  it('rejects a nested location', () => {
    expect(() => parseBlocks(`location /a {
  location /b {
    add_header X-A "1";
  }
}`)).toThrow(/nested/)
  })

  it('leaves no location in the real template unparsed', () => {
    const server = blocks().find((b) => b.name === 'server')!.body
    expect(server).not.toMatch(/^\s*location\b/m)
    for (const b of blocks().filter((x) => x.name !== 'server')) expect(b.body.replace(/\$\{\w+\}/g, '')).not.toMatch(/[{}]/)
  })
})

describe('nginx template listen port', () => {
  it('listens on the unprivileged port 8080', () => {
    expect(template).toMatch(/^\s*listen\s+8080;/m)
  })

  it('does not put the listen port into directory redirects', () => {
    expect(template).toMatch(/^\s*absolute_redirect\s+off;/m)
  })

  it('agrees with the Dockerfile, Compose and CI admin port', () => {
    const repo = resolve(root, '..')
    const read = (rel: string) => readFileSync(resolve(repo, rel), 'utf8')
    expect(read('frontend/Dockerfile')).toMatch(/^EXPOSE 8080\s*$/m)
    const compose = read('docker-compose.yml')
    const admin = compose.slice(compose.indexOf('  admin:'))
    expect(admin).toMatch(/STRUO_ADMIN_PORT:-\d+\}:8080"/)
    expect(read('.github/workflows/ci.yml')).toMatch(/--name admin --publish \d+:8080 /)
  })
})

describe('nginx template security headers', () => {
  it('finds the server block and the add_header locations', () => {
    const names = withHeaders.map((b) => b.name)
    expect(names).toEqual(expect.arrayContaining(['server', '= /index.html', '/assets/', ...PROXIED]))
  })

  it.each(withHeaders.map((b) => [b.name, b] as const))(
    'block %s declares Permissions-Policy and a Content-Security-Policy',
    (_name, b) => {
      expect(b.body).toContain(PERMISSIONS)
      expect(b.body).toMatch(/add_header Content-Security-Policy /)
    },
  )

  it.each(withHeaders.filter((b) => !PROXIED.includes(b.name)).map((b) => [b.name, b] as const))(
    'block %s carries the application policy',
    (_name, b) => {
      expect(b.body).toContain(`add_header ${APP_CSP} always;`)
    },
  )

  it.each(PROXIED)('the %s location carries the sandbox policy and hides the upstream CSP', (name) => {
    const block = blocks().find((b) => b.name === name)!
    expect(block.body).toContain(`add_header ${API_CSP} always;`)
    expect(block.body).toContain('proxy_hide_header Content-Security-Policy;')
  })

  it.each(withHeaders.map((b) => [b.name, b] as const))(
    'block %s sends every security header on every response',
    (_name, b) => {
      for (const h of SECURITY_HEADERS) {
        expect(b.body).toMatch(new RegExp(`add_header ${h} "[^\n]*" always;`))
      }
    },
  )
})

describe('nginx template cache headers', () => {
  const body = (name: string) => blocks().find((b) => b.name === name)!.body

  it.each(['= /index.html', '/'])('block %s revalidates on every request', (name) => {
    expect(body(name)).toContain('add_header Cache-Control "no-cache" always;')
  })

  it('caches only the content-hashed /assets/ files immutably', () => {
    expect(body('/assets/')).toContain('add_header Cache-Control "public, max-age=31536000, immutable"')
  })

  it('leaves the proxied locations without a Cache-Control of their own', () => {
    for (const name of PROXIED) expect(body(name)).not.toContain('Cache-Control')
  })
})

describe('nginx template API proxying', () => {
  it('proxies the OIDC callback paths as exact-match locations', () => {
    const names = blocks().map((b) => b.name)
    expect(names).toContain('= /signin-oidc')
    expect(names).toContain('= /signout-callback-oidc')
  })

  it.each(PROXIED)('the %s location proxies to the upstream and keeps the client host with its port', (name) => {
    const block = blocks().find((b) => b.name === name)!
    expect(block.body).toContain('proxy_pass $upstream;')
    expect(block.body).toContain('set $upstream "${API_UPSTREAM}";')
    expect(block.body).toContain('proxy_set_header Host $http_host;')
    expect(block.body).toContain('proxy_set_header X-Forwarded-Host $http_host;')
    expect(block.body).toContain('proxy_set_header X-Forwarded-Proto $struo_forwarded_proto;')
    expect(block.body).not.toMatch(/\$host/)
  })

  it('maps the incoming X-Forwarded-Proto, falling back to the connection scheme', () => {
    const server = blocks().find((b) => b.name === 'server')!.body
    expect(server).toMatch(/map \$http_x_forwarded_proto \$struo_forwarded_proto \{/)
    expect(server).toMatch(/default\s+\$http_x_forwarded_proto;/)
    expect(server).toMatch(/''\s+\$scheme;/)
  })

  it('every proxied location is covered', () => {
    const proxyPassBlocks = blocks().filter((b) => /proxy_pass/.test(b.body)).map((b) => b.name)
    expect(proxyPassBlocks.sort()).toEqual([...PROXIED].sort())
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
