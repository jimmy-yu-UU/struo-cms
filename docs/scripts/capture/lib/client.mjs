// An HTTP client for the capture run: one cookie jar per identity, the CSRF rule, bearer tokens and
// raw responses. It knows nothing about the CMS beyond the CSRF header, and nothing about chapters.
//
// The CSRF rule (src/Struo.Api/Auth/CsrfProtectionMiddleware.cs): every request other than
// GET/HEAD/OPTIONS/TRACE that carries a session cookie needs a non-empty X-Struo-CSRF header; a
// request without a cookie, or with a bearer token, is exempt. A step can omit the header to
// capture the refusal.
import http from 'node:http'
import https from 'node:https'

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE'])
const CSRF_HEADER = 'X-Struo-CSRF'

export class HostUnreachableError extends Error {}

function updateJar(jar, rawHeaders) {
  for (let i = 0; i < rawHeaders.length; i += 2) {
    if (rawHeaders[i].toLowerCase() !== 'set-cookie') continue
    const [pair] = rawHeaders[i + 1].split(';')
    const at = pair.indexOf('=')
    const name = pair.slice(0, at).trim()
    const value = pair.slice(at + 1).trim()
    if (value === '') jar.delete(name)
    else jar.set(name, value)
  }
}

function send(url, options, body) {
  const transport = url.protocol === 'https:' ? https : http
  return new Promise((resolve, reject) => {
    const req = transport.request(url, options, (res) => {
      const chunks = []
      res.on('data', (chunk) => chunks.push(chunk))
      res.on('end', () => {
        const headers = []
        for (let i = 0; i < res.rawHeaders.length; i += 2) headers.push([res.rawHeaders[i], res.rawHeaders[i + 1]])
        resolve({
          status: res.statusCode,
          statusMessage: res.statusMessage,
          httpVersion: res.httpVersion,
          headers,
          body: Buffer.concat(chunks).toString('utf8'),
        })
      })
    })
    req.on('error', reject)
    req.end(body)
  })
}

export function createClient({ baseUrl, csrfValue = 'capture' }) {
  const jars = new Map()
  // One keep-alive agent per client: the whole run travels over a few long-lived connections. A new
  // connection per request is reset intermittently by some loopback stacks (Windows measured, about
  // one in ten), which the run cannot tell apart from an unreachable host.
  const agents = { 'http:': new http.Agent({ keepAlive: true }), 'https:': new https.Agent({ keepAlive: true }) }

  // identity: the name of a cookie jar, or undefined for a request with no cookie at all.
  async function request({ identity, method, path, body, bearer, csrf = true }) {
    const jar = identity === undefined ? null : (jars.get(identity) ?? jars.set(identity, new Map()).get(identity))
    const headers = {}
    if (body !== undefined) headers['Content-Type'] = 'application/json'
    if (bearer !== undefined) {
      headers.Authorization = `Bearer ${bearer}`
    } else if (jar && jar.size > 0) {
      headers.Cookie = [...jar].map(([name, value]) => `${name}=${value}`).join('; ')
      if (csrf && !SAFE_METHODS.has(method)) headers[CSRF_HEADER] = csrfValue
    }
    let response
    try {
      response = await send(new URL(path, baseUrl), { method, headers, agent: agents[new URL(baseUrl).protocol] }, body)
    } catch (error) {
      throw new HostUnreachableError(`host unreachable at ${baseUrl} (${error.code ?? error.message})`)
    }
    if (jar) updateJar(jar, response.headers.flat())
    return response
  }

  const close = () => Object.values(agents).forEach((agent) => agent.destroy())

  return { request, close }
}
