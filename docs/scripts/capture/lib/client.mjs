// An HTTP client for the capture run: one cookie jar per identity, the CSRF rule, bearer tokens,
// raw responses and a request timeout. It knows nothing about the CMS beyond the CSRF header, and
// nothing about chapters.
//
// The CSRF rule (src/Struo.Api/Auth/CsrfProtectionMiddleware.cs): every request other than
// GET/HEAD/OPTIONS/TRACE that carries a session cookie needs a non-empty X-Struo-CSRF header; a
// request without a cookie, or with a bearer token, is exempt. A step can omit the header to
// capture the refusal.
import http from 'node:http'
import https from 'node:https'

const SAFE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS', 'TRACE'])
const RETRYABLE_METHODS = new Set(['GET', 'HEAD', 'OPTIONS'])
const CSRF_HEADER = 'X-Struo-CSRF'
const DEFAULT_TIMEOUT_MS = 30000
const UNREACHABLE = new Set(['ECONNREFUSED', 'ENOTFOUND', 'EAI_AGAIN', 'EHOSTUNREACH', 'ENETUNREACH'])

export class HostUnreachableError extends Error {}
export class ConnectionError extends Error {}
export class RequestTimeoutError extends Error {}

const CUT_OFF = 'response cut off before its body was complete'

function cookieEnds(attributes) {
  return attributes.some((attribute) => {
    const [name, value = ''] = attribute.split('=').map((part) => part.trim())
    if (name.toLowerCase() === 'max-age') return Number(value) <= 0
    if (name.toLowerCase() === 'expires') return Date.parse(value) <= Date.now()
    return false
  })
}

function updateJar(jar, headers) {
  for (const [header, value] of headers) {
    if (header.toLowerCase() !== 'set-cookie') continue
    const [pair, ...attributes] = value.split(';')
    const at = pair.indexOf('=')
    const name = pair.slice(0, at).trim()
    const cookie = pair.slice(at + 1).trim()
    if (cookie === '' || cookieEnds(attributes)) jar.delete(name)
    else jar.set(name, cookie)
  }
}

function describe(error, baseUrl) {
  if (error instanceof RequestTimeoutError) return error
  if (UNREACHABLE.has(error.code)) return new HostUnreachableError(`host unreachable at ${baseUrl} (${error.code})`)
  return new ConnectionError(`connection to ${baseUrl} failed (${error.detail ?? error.code ?? error.message})`)
}

function attempt(url, options, body, timeoutMs) {
  const transport = url.protocol === 'https:' ? https : http
  return new Promise((resolve, reject) => {
    let settled = false
    const finish = (settle, value) => {
      if (settled) return
      settled = true
      clearTimeout(timer)
      settle(value)
    }
    // Once the headers have arrived the server has the request: a failure from then on is never a
    // stale-socket reset, and a body that stops short must not settle as a response. The socket
    // error reaches the request and the response in either order, so both paths report it the same way.
    let started = null
    const cutOff = () =>
      finish(reject, Object.assign(new Error(CUT_OFF), { code: 'ECONNRESET', detail: CUT_OFF, reusedSocket: false }))
    const req = transport.request(url, options, (res) => {
      started = res
      const chunks = []
      res.on('data', (chunk) => chunks.push(chunk))
      res.on('error', cutOff)
      res.on('aborted', cutOff)
      res.on('close', () => {
        if (!res.complete) cutOff()
      })
      res.on('end', () => {
        const headers = []
        for (let i = 0; i < res.rawHeaders.length; i += 2) headers.push([res.rawHeaders[i], res.rawHeaders[i + 1]])
        const bytes = Buffer.concat(chunks)
        finish(resolve, {
          status: res.statusCode,
          statusMessage: res.statusMessage,
          httpVersion: res.httpVersion,
          headers,
          bytes,
          body: bytes.toString('utf8'),
        })
      })
    })
    const timer = setTimeout(
      () => req.destroy(new RequestTimeoutError(`no answer within ${timeoutMs} ms`)),
      timeoutMs,
    )
    req.on('error', (error) => {
      if (started && !started.complete && !(error instanceof RequestTimeoutError)) cutOff()
      else finish(reject, Object.assign(error, { reusedSocket: req.reusedSocket }))
    })
    req.end(body)
  })
}

export function createClient({ baseUrl, csrfValue = 'capture', timeoutMs = DEFAULT_TIMEOUT_MS }) {
  const jars = new Map()
  // One keep-alive agent per client: the run travels over one long-lived connection instead of a
  // new one per request (docs/ai/decisions/capture-client-reuses-one-connection.md has why). A
  // server can close an idle kept-alive socket just as the next request goes out, so an idempotent
  // request (GET, HEAD, OPTIONS) is retried once when a reused socket is reset. Any other method
  // fails the step: a reset does not show that the server never processed it, and a repeated
  // create or delete would shift the ids and totals the manual prints.
  const agents = { 'http:': new http.Agent({ keepAlive: true }), 'https:': new https.Agent({ keepAlive: true }) }

  // identity: the name of a cookie jar, or undefined for a request with no cookie at all.
  async function request({ identity, method, path, body, contentType, bearer, csrf = true, timeoutMs: stepTimeout }) {
    const jar = identity === undefined ? null : (jars.get(identity) ?? jars.set(identity, new Map()).get(identity))
    const headers = {}
    if (body !== undefined) headers['Content-Type'] = contentType ?? 'application/json'
    if (bearer !== undefined) {
      headers.Authorization = `Bearer ${bearer}`
    } else if (jar && jar.size > 0) {
      headers.Cookie = [...jar].map(([name, value]) => `${name}=${value}`).join('; ')
      if (csrf && !SAFE_METHODS.has(method)) headers[CSRF_HEADER] = csrfValue
    }
    const url = new URL(path, baseUrl)
    const options = { method, headers, agent: agents[url.protocol] }
    const limit = stepTimeout ?? timeoutMs
    let response
    try {
      try {
        response = await attempt(url, options, body, limit)
      } catch (error) {
        if (error.code !== 'ECONNRESET' || !error.reusedSocket || !RETRYABLE_METHODS.has(method)) throw error
        response = await attempt(url, options, body, limit)
      }
    } catch (error) {
      throw describe(error, baseUrl)
    }
    if (jar) updateJar(jar, response.headers)
    return response
  }

  const close = () => Object.values(agents).forEach((agent) => agent.destroy())

  // The cookie values each identity holds, for the runner to treat as secrets.
  const sessions = () => new Map([...jars].map(([identity, jar]) => [identity, [...jar.values()]]))

  return { request, sessions, close }
}
